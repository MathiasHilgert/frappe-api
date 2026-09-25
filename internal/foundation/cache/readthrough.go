package cache

import (
	"context"
	"errors"
	"fmt"
	"math/rand/v2"
	"reflect"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"
)

// DefaultJitter is the fraction of the lifetime added at random to every
// entry unless the Jitter option says otherwise, so entries written
// together do not all expire together.
const DefaultJitter = 0.1

// segmentPattern matches one lowercase entry name segment, the same rule
// as telemetry and event names.
var segmentPattern = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)

// registeredNames holds every entry name created in this process, so two
// entries can never share (and corrupt) one key space.
var registeredNames sync.Map

type options struct {
	codec   ValueCodec
	jitter  float64
	version int
	global  bool
}

// Option customizes a ReadThrough created by New.
type Option func(*options)

// Global makes an entry shared by every tenant. Use it only for data that
// is not tenant-owned; entries are tenant-scoped by default.
func Global() Option {
	return func(settings *options) { settings.global = true }
}

// Version sets the entry's schema version (default 1). The version is
// part of every key, so bumping it orphans all old entries at once: bump
// it whenever the cached type's shape changes. Old entries expire on
// their own.
func Version(version int) Option {
	return func(settings *options) { settings.version = version }
}

// Codec replaces the default JSONCodec.
func Codec(codec ValueCodec) Option {
	return func(settings *options) { settings.codec = codec }
}

// Jitter sets the fraction, between 0 and 1, of the lifetime added at
// random to each write. Defaults to DefaultJitter; Jitter(0) disables it.
func Jitter(fraction float64) Option {
	return func(settings *options) { settings.jitter = fraction }
}

// ReadThrough caches the results of one load function: Get returns the
// cached value or calls load, stores its result and returns it. It is
// built once, in a module adapter, and is safe for concurrent use.
type ReadThrough[K Key, V any] struct {
	backend    *Backend
	load       func(context.Context, K) (V, error)
	codec      ValueCodec
	name       string
	module     string
	version    string
	timeToLive time.Duration
	jitter     float64
	global     bool
}

// New builds the ReadThrough named name ("<module>.<entry>", lowercase dot
// separated segments; the module segment labels the metrics) that caches
// load for timeToLive (zero means the backend default). It panics on an
// invalid or duplicate name, an unsupported key type or invalid options:
// these are programming errors caught at startup. backend may be nil,
// which disables caching.
func New[K Key, V any](backend *Backend, name string, timeToLive time.Duration, load func(context.Context, K) (V, error), optionFunctions ...Option) *ReadThrough[K, V] {
	settings := options{codec: JSONCodec{}, jitter: DefaultJitter, version: 1}
	for _, apply := range optionFunctions {
		apply(&settings)
	}
	if err := validate(name, timeToLive, load != nil, reflect.TypeFor[K](), settings); err != nil {
		panic(err)
	}
	if _, duplicate := registeredNames.LoadOrStore(name, struct{}{}); duplicate {
		panic(fmt.Sprintf("cache: duplicate entry name %q", name))
	}
	if backend != nil && timeToLive == 0 {
		timeToLive = backend.timeToLive
	}
	return &ReadThrough[K, V]{
		backend:    backend,
		load:       load,
		codec:      settings.codec,
		name:       name,
		module:     strings.SplitN(name, ".", 2)[0],
		version:    "v" + strconv.Itoa(settings.version),
		timeToLive: timeToLive,
		jitter:     settings.jitter,
		global:     settings.global,
	}
}

func validateName(name string) error {
	segments := strings.Split(name, ".")
	if len(segments) < 2 {
		return fmt.Errorf("cache: entry name %q must be <module>.<entry>", name)
	}
	for _, segment := range segments {
		if !segmentPattern.MatchString(segment) {
			return fmt.Errorf("cache: invalid entry name %q: segment %q must match %s", name, segment, segmentPattern)
		}
	}
	return nil
}

func validate(name string, timeToLive time.Duration, hasLoad bool, keyType reflect.Type, settings options) error {
	switch {
	case validateName(name) != nil:
		return validateName(name)
	case !hasLoad:
		return fmt.Errorf("cache: entry %q needs a load function", name)
	case timeToLive < 0:
		return fmt.Errorf("cache: entry %q has a negative lifetime", name)
	case settings.version < 1:
		return fmt.Errorf("cache: entry %q version must be at least 1", name)
	case settings.jitter < 0 || settings.jitter > 1:
		return fmt.Errorf("cache: entry %q jitter must be between 0 and 1", name)
	case settings.codec == nil:
		return fmt.Errorf("cache: entry %q needs a codec", name)
	case !supportedKeyType(keyType):
		return fmt.Errorf("cache: entry %q key type %s is not supported; implement cache.KeyEncoder", name, keyType)
	}
	return nil
}

// Get returns the value for key, from the cache when present and from load
// otherwise. Concurrent misses for the same key share one load (the first
// caller's context runs it). Load errors are returned and never cached.
// Store failures never fail Get: the value is loaded instead (fail open).
// A tenant-scoped entry without a tenant in ctx skips the cache entirely
// (fail closed for caching).
func (readThrough *ReadThrough[K, V]) Get(ctx context.Context, key K) (V, error) {
	backend := readThrough.backend
	if backend == nil {
		return readThrough.load(ctx, key)
	}
	storageKey, err := readThrough.storageKey(tenantFrom(ctx, backend.tenantResolver), key)
	if err != nil {
		readThrough.bypass(ctx, err)
		return readThrough.timedLoad(ctx, key)
	}
	result, err, _ := backend.group.Do(storageKey, func() (any, error) {
		return readThrough.fetch(ctx, storageKey, key)
	})
	if err != nil {
		var zero V
		return zero, err
	}
	fetched := result.(fetchResult[V])
	recordRequest(ctx, readThrough.module, fetched.outcome)
	return fetched.value, nil
}

// Invalidate deletes the entries of keys for the tenant in ctx. Unlike
// Get it reports store failures, so a caller (typically an event
// consumer) can retry.
func (readThrough *ReadThrough[K, V]) Invalidate(ctx context.Context, keys ...K) error {
	if readThrough.backend == nil {
		return nil
	}
	return readThrough.invalidate(ctx, tenantFrom(ctx, readThrough.backend.tenantResolver), keys)
}

// InvalidateFor deletes the entries of keys for an explicit tenant. It is
// the only entry point that takes a tenant by hand and exists for event
// consumers, whose context carries no request tenant: the tenant comes
// from the event itself. Pass "" for a Global entry.
func (readThrough *ReadThrough[K, V]) InvalidateFor(ctx context.Context, tenant string, keys ...K) error {
	if readThrough.backend == nil {
		return nil
	}
	return readThrough.invalidate(ctx, tenant, keys)
}

// Name returns the entry name.
func (readThrough *ReadThrough[K, V]) Name() string {
	return readThrough.name
}

func (readThrough *ReadThrough[K, V]) invalidate(ctx context.Context, tenant string, keys []K) error {
	storageKeys := make([]string, 0, len(keys))
	for _, key := range keys {
		storageKey, err := readThrough.storageKey(tenant, key)
		if err != nil {
			return fmt.Errorf("cache: invalidate %s: %w", readThrough.name, err)
		}
		storageKeys = append(storageKeys, storageKey)
	}
	if len(storageKeys) == 0 {
		return nil
	}
	if err := readThrough.backend.delete(ctx, storageKeys...); err != nil {
		readThrough.backend.failure(ctx, readThrough.name, operationDelete, reasonStoreError, err)
		return fmt.Errorf("cache: invalidate %s: %w", readThrough.name, err)
	}
	return nil
}

// storageKey builds frappe:<global|tenant:<tenant>>:<name>:v<version>:<key>.
func (readThrough *ReadThrough[K, V]) storageKey(tenant string, key K) (string, error) {
	scope, err := scopeSegment(readThrough.global, tenant)
	if err != nil {
		return "", err
	}
	encoded, ok := encodeKey(reflect.ValueOf(key))
	if !ok {
		return "", errInvalidKey
	}
	return keyPrefix + scope + ":" + readThrough.name + ":" + readThrough.version + ":" + encoded, nil
}

// bypass records why Get skipped the cache.
func (readThrough *ReadThrough[K, V]) bypass(ctx context.Context, err error) {
	reason := reasonInvalidKey
	if errors.Is(err, ErrMissingTenant) {
		reason = reasonMissingTenant
	}
	readThrough.backend.failure(ctx, readThrough.name, operationKey, reason, err)
	recordRequest(ctx, readThrough.module, outcomeError)
}

type fetchResult[V any] struct {
	value   V
	outcome string
}

// fetch reads storageKey and falls back to load, storing what it loaded.
func (readThrough *ReadThrough[K, V]) fetch(ctx context.Context, storageKey string, key K) (fetchResult[V], error) {
	value, outcome, found := readThrough.read(ctx, storageKey)
	if found {
		return fetchResult[V]{value: value, outcome: outcome}, nil
	}
	loaded, err := readThrough.timedLoad(ctx, key)
	if err != nil {
		return fetchResult[V]{}, err
	}
	readThrough.write(ctx, storageKey, loaded)
	return fetchResult[V]{value: loaded, outcome: outcome}, nil
}

// read returns the cached value and whether it was usable, with the
// request outcome. Corrupted entries are deleted.
func (readThrough *ReadThrough[K, V]) read(ctx context.Context, storageKey string) (V, string, bool) {
	var value V
	backend := readThrough.backend
	data, found, err := backend.get(ctx, storageKey)
	if err != nil {
		backend.failure(ctx, readThrough.name, operationGet, reasonStoreError, err)
		return value, outcomeError, false
	}
	if !found {
		return value, outcomeMiss, false
	}
	if err := readThrough.codec.Decode(data, &value); err != nil {
		backend.failure(ctx, readThrough.name, operationDecode, reasonCorruptedEntry, err)
		if err := backend.delete(ctx, storageKey); err != nil {
			backend.failure(ctx, readThrough.name, operationDelete, reasonStoreError, err)
		}
		var zero V
		return zero, outcomeError, false
	}
	return value, outcomeHit, true
}

// write stores value, logging and counting failures without returning them.
func (readThrough *ReadThrough[K, V]) write(ctx context.Context, storageKey string, value V) {
	backend := readThrough.backend
	data, err := readThrough.codec.Encode(value)
	if err != nil {
		backend.failure(ctx, readThrough.name, operationEncode, reasonEncodeError, err)
		return
	}
	if err := backend.set(ctx, storageKey, data, readThrough.lifetime()); err != nil {
		backend.failure(ctx, readThrough.name, operationSet, reasonStoreError, err)
	}
}

func (readThrough *ReadThrough[K, V]) timedLoad(ctx context.Context, key K) (V, error) {
	started := time.Now()
	defer recordLoad(ctx, readThrough.module, started)
	return readThrough.load(ctx, key)
}

// lifetime returns the entry lifetime plus a random jitter.
func (readThrough *ReadThrough[K, V]) lifetime() time.Duration {
	spread := int64(float64(readThrough.timeToLive) * readThrough.jitter)
	if spread <= 0 {
		return readThrough.timeToLive
	}
	return readThrough.timeToLive + time.Duration(rand.Int64N(spread)) //nolint:gosec // Expiry jitter needs no cryptographic randomness.
}
