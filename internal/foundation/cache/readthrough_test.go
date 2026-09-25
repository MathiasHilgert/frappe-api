package cache_test

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/cachetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/memory"
)

// entryCounter makes every entry name unique, so tests can build new
// ReadThroughs even when the package runs more than once (-count=2).
var entryCounter atomic.Int64

func uniqueName(module string) string {
	return fmt.Sprintf("%s.entry_%d", module, entryCounter.Add(1))
}

type menu struct {
	Name  string `json:"name"`
	Price int    `json:"price"`
}

// spyStore wraps a memory store and records every Set.
type spyStore struct {
	*memory.Store
	lifetimes map[string]time.Duration
	mutex     sync.Mutex
}

func newSpyStore() *spyStore {
	return &spyStore{Store: memory.NewStore(), lifetimes: map[string]time.Duration{}}
}

func (store *spyStore) Set(ctx context.Context, key string, value []byte, timeToLive time.Duration) error {
	store.mutex.Lock()
	store.lifetimes[key] = timeToLive
	store.mutex.Unlock()
	return store.Store.Set(ctx, key, value, timeToLive)
}

func (store *spyStore) keys() []string {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	keys := make([]string, 0, len(store.lifetimes))
	for key := range store.lifetimes {
		keys = append(keys, key)
	}
	return keys
}

// failingStore fails every operation.
type failingStore struct{}

func (failingStore) Get(context.Context, string) ([]byte, bool, error) {
	return nil, false, errors.New("store down")
}

func (failingStore) Set(context.Context, string, []byte, time.Duration) error {
	return errors.New("store down")
}

func (failingStore) Delete(context.Context, ...string) error { return errors.New("store down") }

// countingLoader returns a loader that counts its calls.
func countingLoader(calls *atomic.Int64) func(context.Context, int) (menu, error) {
	return func(_ context.Context, id int) (menu, error) {
		calls.Add(1)
		return menu{Name: fmt.Sprintf("menu %d", id), Price: id}, nil
	}
}

func newBackend(store cache.Store) *cache.Backend {
	return cache.NewBackend(store, cache.Settings{TenantResolver: cachetest.TenantResolver})
}

func acme() context.Context {
	return cachetest.WithTenant(context.Background(), "acme")
}

func TestGetLoadsOnceThenHits(t *testing.T) {
	var calls atomic.Int64
	byID := cache.New(newBackend(memory.NewStore()), uniqueName("menu"), time.Minute, countingLoader(&calls))

	for range 3 {
		value, err := byID.Get(acme(), 7)
		if err != nil || value != (menu{Name: "menu 7", Price: 7}) {
			t.Fatalf("Get = %+v, %v; want menu 7", value, err)
		}
	}
	if calls.Load() != 1 {
		t.Fatalf("loader called %d times; want 1", calls.Load())
	}
}

func TestKeysCarryTenantNameVersionAndEscapedKey(t *testing.T) {
	store := newSpyStore()
	name := uniqueName("menu")
	byName := cache.New(newBackend(store), name, time.Minute,
		func(context.Context, string) (menu, error) { return menu{}, nil }, cache.Version(3), cache.Jitter(0))

	if _, err := byName.Get(cachetest.WithTenant(context.Background(), "a:b"), "x:y%z"); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	want := "frappe:tenant:a%3Ab:" + name + ":v3:x%3Ay%25z"
	if keys := store.keys(); len(keys) != 1 || keys[0] != want {
		t.Fatalf("stored keys = %v; want [%s]", keys, want)
	}
}

func TestTenantsNeverShareEntries(t *testing.T) {
	byID := cache.New(newBackend(memory.NewStore()), uniqueName("menu"), time.Minute,
		func(ctx context.Context, id int) (menu, error) {
			tenant, _ := cachetest.TenantResolver(ctx)
			return menu{Name: tenant, Price: id}, nil
		})

	for _, tenant := range []string{"acme", "globex"} {
		value, err := byID.Get(cachetest.WithTenant(context.Background(), tenant), 1)
		if err != nil || value.Name != tenant {
			t.Fatalf("Get for %s = %+v, %v; want that tenant's value", tenant, value, err)
		}
	}
}

func TestMissingTenantBypassesTheCache(t *testing.T) {
	store := newSpyStore()
	var calls atomic.Int64
	byID := cache.New(newBackend(store), uniqueName("menu"), time.Minute, countingLoader(&calls))

	for range 2 {
		if _, err := byID.Get(context.Background(), 1); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}
	if calls.Load() != 2 || len(store.keys()) != 0 {
		t.Fatalf("loader calls = %d, stored keys = %v; want 2 loads and nothing stored", calls.Load(), store.keys())
	}
	if err := byID.Invalidate(context.Background(), 1); !errors.Is(err, cache.ErrMissingTenant) {
		t.Fatalf("Invalidate without tenant = %v; want ErrMissingTenant", err)
	}
}

func TestGlobalEntriesNeedNoTenant(t *testing.T) {
	store := newSpyStore()
	name := uniqueName("catalog")
	var calls atomic.Int64
	byID := cache.New(newBackend(store), name, time.Minute, countingLoader(&calls), cache.Global(), cache.Jitter(0))

	for range 2 {
		if _, err := byID.Get(context.Background(), 5); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}
	if keys := store.keys(); calls.Load() != 1 || len(keys) != 1 || keys[0] != "frappe:global:"+name+":v1:5" {
		t.Fatalf("calls = %d, keys = %v; want one load under the global key", calls.Load(), keys)
	}
	if err := byID.InvalidateFor(context.Background(), "acme", 5); err == nil {
		t.Fatal("InvalidateFor with a tenant on a global entry returned nil error")
	}
}

func TestLoadErrorsAreNotCached(t *testing.T) {
	var calls atomic.Int64
	failure := errors.New("not found")
	byID := cache.New(newBackend(memory.NewStore()), uniqueName("menu"), time.Minute,
		func(context.Context, int) (menu, error) {
			calls.Add(1)
			return menu{}, failure
		})

	for range 2 {
		if _, err := byID.Get(acme(), 1); !errors.Is(err, failure) {
			t.Fatalf("Get = %v; want the load error", err)
		}
	}
	if calls.Load() != 2 {
		t.Fatalf("loader called %d times; want 2 (errors are never cached)", calls.Load())
	}
}

func TestStoreFailuresFailOpen(t *testing.T) {
	var calls atomic.Int64
	byID := cache.New(newBackend(failingStore{}), uniqueName("menu"), time.Minute, countingLoader(&calls))

	value, err := byID.Get(acme(), 9)
	if err != nil || value.Price != 9 {
		t.Fatalf("Get = %+v, %v; want the loaded value despite the store failing", value, err)
	}
	if err := byID.Invalidate(acme(), 9); err == nil {
		t.Fatal("Invalidate returned nil error while the store is down")
	}
}

func TestCorruptedEntriesAreReloaded(t *testing.T) {
	store := newSpyStore()
	var calls atomic.Int64
	byID := cache.New(newBackend(store), uniqueName("menu"), time.Minute, countingLoader(&calls))
	if _, err := byID.Get(acme(), 2); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	if err := store.Store.Set(context.Background(), store.keys()[0], []byte("{not json"), time.Minute); err != nil {
		t.Fatalf("corrupting entry: %v", err)
	}

	value, err := byID.Get(acme(), 2)
	if err != nil || value.Price != 2 || calls.Load() != 2 {
		t.Fatalf("Get = %+v, %v after %d loads; want a reload of menu 2", value, err, calls.Load())
	}
	if _, err := byID.Get(acme(), 2); err != nil || calls.Load() != 2 {
		t.Fatalf("the reloaded entry was not stored again (loads = %d, err = %v)", calls.Load(), err)
	}
}

func TestConcurrentMissesLoadOnce(t *testing.T) {
	var calls atomic.Int64
	release := make(chan struct{})
	byID := cache.New(newBackend(memory.NewStore()), uniqueName("menu"), time.Minute,
		func(_ context.Context, id int) (menu, error) {
			calls.Add(1)
			<-release
			return menu{Price: id}, nil
		})

	var group sync.WaitGroup
	for range 20 {
		group.Go(func() {
			if value, err := byID.Get(acme(), 4); err != nil || value.Price != 4 {
				t.Errorf("Get = %+v, %v; want menu 4", value, err)
			}
		})
	}
	time.Sleep(50 * time.Millisecond)
	close(release)
	group.Wait()
	if calls.Load() != 1 {
		t.Fatalf("loader called %d times for concurrent misses; want 1", calls.Load())
	}
}

func TestInvalidateForcesAReload(t *testing.T) {
	var calls atomic.Int64
	byID := cache.New(newBackend(memory.NewStore()), uniqueName("menu"), time.Minute, countingLoader(&calls))
	get := func() {
		if _, err := byID.Get(acme(), 1); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}

	get()
	if err := byID.Invalidate(acme(), 1); err != nil {
		t.Fatalf("Invalidate returned error: %v", err)
	}
	get()
	if err := byID.InvalidateFor(context.Background(), "acme", 1); err != nil {
		t.Fatalf("InvalidateFor returned error: %v", err)
	}
	get()
	if calls.Load() != 3 {
		t.Fatalf("loader called %d times; want 3 (one per invalidation)", calls.Load())
	}
	if err := byID.InvalidateFor(context.Background(), "", 1); !errors.Is(err, cache.ErrMissingTenant) {
		t.Fatalf("InvalidateFor with an empty tenant = %v; want ErrMissingTenant", err)
	}
}

func TestVersionSeparatesEntries(t *testing.T) {
	store := newSpyStore()
	name := uniqueName("menu")
	load := func(context.Context, int) (menu, error) { return menu{}, nil }
	first := cache.New(newBackend(store), name, time.Minute, load)
	if _, err := first.Get(acme(), 1); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	keys := store.keys()
	if len(keys) != 1 || !strings.Contains(keys[0], ":v1:") {
		t.Fatalf("keys = %v; want one v1 key", keys)
	}
}

func TestJitterSpreadsLifetimes(t *testing.T) {
	store := newSpyStore()
	byID := cache.New(newBackend(store), uniqueName("menu"), time.Minute,
		func(context.Context, int) (menu, error) { return menu{}, nil }, cache.Jitter(0.5))
	for id := range 50 {
		if _, err := byID.Get(acme(), id); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}
	distinct := map[time.Duration]struct{}{}
	for _, lifetime := range store.lifetimes {
		if lifetime < time.Minute || lifetime >= 90*time.Second {
			t.Fatalf("lifetime %s outside [1m, 1m30s)", lifetime)
		}
		distinct[lifetime] = struct{}{}
	}
	if len(distinct) < 2 {
		t.Fatal("jitter produced identical lifetimes")
	}
}

func TestZeroLifetimeUsesTheBackendDefault(t *testing.T) {
	store := newSpyStore()
	backend := cache.NewBackend(store, cache.Settings{TenantResolver: cachetest.TenantResolver, DefaultTimeToLive: 42 * time.Second})
	byID := cache.New(backend, uniqueName("menu"), 0,
		func(context.Context, int) (menu, error) { return menu{}, nil }, cache.Jitter(0))
	if _, err := byID.Get(acme(), 1); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	for _, lifetime := range store.lifetimes {
		if lifetime != 42*time.Second {
			t.Fatalf("lifetime = %s; want the 42s default", lifetime)
		}
	}
}

func TestNilBackendAlwaysLoads(t *testing.T) {
	var calls atomic.Int64
	byID := cache.New(nil, uniqueName("menu"), time.Minute, countingLoader(&calls))
	for range 2 {
		if _, err := byID.Get(acme(), 1); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}
	if calls.Load() != 2 {
		t.Fatalf("loader called %d times; want 2", calls.Load())
	}
	if err := byID.Invalidate(acme(), 1); err != nil {
		t.Fatalf("Invalidate on a nil backend returned error: %v", err)
	}
}

type menuKey struct{ Region, Slug string }

func (key menuKey) CacheKey() string { return key.Region + "/" + key.Slug }

func TestSupportedKeyTypes(t *testing.T) {
	store := newSpyStore()
	backend := newBackend(store)
	load := func(context.Context, uuid.UUID) (menu, error) { return menu{}, nil }
	identifier := uuid.MustParse("7f1c1a5e-6f0e-4c56-9d38-2f5c1a3b0e11")
	if _, err := cache.New(backend, uniqueName("menu"), time.Minute, load).Get(acme(), identifier); err != nil {
		t.Fatalf("uuid key: %v", err)
	}
	custom := cache.New(backend, uniqueName("menu"), time.Minute, func(context.Context, menuKey) (menu, error) { return menu{}, nil })
	if _, err := custom.Get(acme(), menuKey{Region: "eu", Slug: "lunch"}); err != nil {
		t.Fatalf("CacheKey key: %v", err)
	}
	joined := strings.Join(store.keys(), " ")
	for _, fragment := range []string{identifier.String(), "eu/lunch"} {
		if !strings.Contains(joined, fragment) {
			t.Fatalf("keys %v do not contain %q", store.keys(), fragment)
		}
	}
}

func TestEmptyKeysBypassTheCache(t *testing.T) {
	store := newSpyStore()
	byName := cache.New(newBackend(store), uniqueName("menu"), time.Minute,
		func(context.Context, string) (menu, error) { return menu{}, nil })
	if _, err := byName.Get(acme(), ""); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	if len(store.keys()) != 0 {
		t.Fatalf("an empty key was stored: %v", store.keys())
	}
}

func TestNewPanicsOnMisuse(t *testing.T) {
	load := func(context.Context, int) (menu, error) { return menu{}, nil }
	duplicate := uniqueName("menu")
	cache.New(nil, duplicate, time.Minute, load)
	cases := map[string]func(){
		"single segment name": func() { cache.New(nil, "menu", time.Minute, load) },
		"uppercase name":      func() { cache.New(nil, "Menu.ById", time.Minute, load) },
		"colon in name":       func() { cache.New(nil, "menu.by:id", time.Minute, load) },
		"duplicate name":      func() { cache.New(nil, duplicate, time.Minute, load) },
		"nil loader":          func() { cache.New[int, menu](nil, uniqueName("menu"), time.Minute, nil) },
		"negative lifetime":   func() { cache.New(nil, uniqueName("menu"), -time.Second, load) },
		"zero version":        func() { cache.New(nil, uniqueName("menu"), time.Minute, load, cache.Version(0)) },
		"jitter above one":    func() { cache.New(nil, uniqueName("menu"), time.Minute, load, cache.Jitter(1.5)) },
		"unsupported key": func() {
			cache.New(nil, uniqueName("menu"), time.Minute, func(context.Context, float64) (menu, error) { return menu{}, nil })
		},
	}
	for name, create := range cases {
		t.Run(name, func(t *testing.T) {
			defer func() {
				if recover() == nil {
					t.Fatal("New did not panic")
				}
			}()
			create()
		})
	}
}
