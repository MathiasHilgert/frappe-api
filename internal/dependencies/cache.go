package dependencies

import (
	"context"
	"errors"
	"time"

	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/memory"
	cachevalkey "github.com/MathiasHilgert/frappe-api/internal/foundation/cache/valkey"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// errValkeyNotReady is returned while the shared Valkey client has not
// come up yet (or has gone down); the cache then fails open.
var errValkeyNotReady = errors.New("valkey client is not ready")

// tenantResolver resolves the request tenant for tenant-scoped cache
// entries. The tenant model is not defined yet, so it is nil and every
// tenant-scoped entry skips the cache (fail closed); wire the resolver
// that reads the same tenant that drives row level security here once it
// exists. Global entries are cached regardless.
var tenantResolver cache.TenantResolver

// provideCache builds the cache.Backend every module receives through its
// Dependencies, following CACHE_*. Disabled, it uses cache.DisabledStore,
// so cache decorators keep working and simply always load. The valkey
// store shares the client registered by provideValkey.
func provideCache(settings configuration.Cache, client *application.Handle[valkeygo.Client]) *cache.Backend {
	backendSettings := cache.Settings{
		TenantResolver:    tenantResolver,
		DefaultTimeToLive: settings.DefaultTimeToLive,
		OperationTimeout:  settings.OperationTimeout,
	}
	return cache.NewBackend(cacheStore(settings, client), backendSettings)
}

func cacheStore(settings configuration.Cache, client *application.Handle[valkeygo.Client]) cache.Store {
	switch {
	case !settings.Enabled:
		return cache.DisabledStore{}
	case settings.Store == configuration.CacheStoreValkey && client != nil:
		return valkeyHandleStore{client: client}
	default:
		return memory.NewStore()
	}
}

// valkeyHandleStore resolves the shared Valkey client on every call,
// because the Backend is built before the client's Up has run.
type valkeyHandleStore struct {
	client *application.Handle[valkeygo.Client]
}

func (store valkeyHandleStore) resolve() (*cachevalkey.Store, error) {
	client, ready := store.client.Get()
	if !ready || client == nil {
		return nil, errValkeyNotReady
	}
	return cachevalkey.NewStore(client, cachevalkey.Settings{}), nil
}

// Get implements cache.Store.
func (store valkeyHandleStore) Get(ctx context.Context, key string) ([]byte, bool, error) {
	resolved, err := store.resolve()
	if err != nil {
		return nil, false, err
	}
	return resolved.Get(ctx, key)
}

// Set implements cache.Store.
func (store valkeyHandleStore) Set(ctx context.Context, key string, value []byte, timeToLive time.Duration) error {
	resolved, err := store.resolve()
	if err != nil {
		return err
	}
	return resolved.Set(ctx, key, value, timeToLive)
}

// Delete implements cache.Store.
func (store valkeyHandleStore) Delete(ctx context.Context, keys ...string) error {
	resolved, err := store.resolve()
	if err != nil {
		return err
	}
	return resolved.Delete(ctx, keys...)
}
