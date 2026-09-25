package cache

import (
	"context"
	"log/slog"
	"time"

	"golang.org/x/sync/singleflight"
)

// Defaults applied by NewBackend to zero-valued Settings fields.
const (
	DefaultTimeToLive       = 5 * time.Minute
	DefaultOperationTimeout = 100 * time.Millisecond
)

// TenantResolver returns the tenant of the current request, the same
// tenant that drives row level security. The composition root injects it
// once; tenant-scoped entries never read or write without it.
type TenantResolver func(ctx context.Context) (tenant string, found bool)

// Settings configures a Backend.
type Settings struct {
	// Logger receives throttled warnings about bypassed failures. Defaults
	// to slog.Default().
	Logger *slog.Logger
	// TenantResolver resolves the tenant of tenant-scoped entries. When
	// nil, every tenant-scoped read skips the cache.
	TenantResolver TenantResolver
	// DefaultTimeToLive applies to entries created with a zero lifetime.
	// Defaults to DefaultTimeToLive.
	DefaultTimeToLive time.Duration
	// OperationTimeout bounds each store round trip, so an unhealthy store
	// adds at most this much latency before the read fails open. Defaults
	// to DefaultOperationTimeout.
	OperationTimeout time.Duration
}

// Backend is the cache handle the composition root builds once and hands
// to every module through its Dependencies: a Store plus the tenant
// resolver, timeouts, logging and the singleflight group shared by every
// ReadThrough built on it. A nil *Backend is valid and disables caching.
type Backend struct {
	store          Store
	tenantResolver TenantResolver
	errorLog       *throttledLog
	group          singleflight.Group
	timeToLive     time.Duration
	timeout        time.Duration
}

// NewBackend returns a Backend over store.
func NewBackend(store Store, settings Settings) *Backend {
	if settings.Logger == nil {
		settings.Logger = slog.Default()
	}
	if settings.DefaultTimeToLive <= 0 {
		settings.DefaultTimeToLive = DefaultTimeToLive
	}
	if settings.OperationTimeout <= 0 {
		settings.OperationTimeout = DefaultOperationTimeout
	}
	return &Backend{
		store:          store,
		tenantResolver: settings.TenantResolver,
		errorLog:       &throttledLog{logger: settings.Logger, interval: errorLogInterval},
		timeToLive:     settings.DefaultTimeToLive,
		timeout:        settings.OperationTimeout,
	}
}

func (backend *Backend) get(ctx context.Context, key string) ([]byte, bool, error) {
	ctx, cancel := context.WithTimeout(ctx, backend.timeout)
	defer cancel()
	return backend.store.Get(ctx, key)
}

func (backend *Backend) set(ctx context.Context, key string, value []byte, timeToLive time.Duration) error {
	ctx, cancel := context.WithTimeout(ctx, backend.timeout)
	defer cancel()
	return backend.store.Set(ctx, key, value, timeToLive)
}

func (backend *Backend) delete(ctx context.Context, keys ...string) error {
	ctx, cancel := context.WithTimeout(ctx, backend.timeout)
	defer cancel()
	return backend.store.Delete(ctx, keys...)
}
