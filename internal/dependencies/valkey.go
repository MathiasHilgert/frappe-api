package dependencies

import (
	"context"

	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/valkey"
)

// needsValkey reports whether any feature uses Valkey: rate limiting, or
// an enabled cache on the valkey store.
func needsValkey(loadedConfiguration configuration.Configuration) bool {
	cacheOnValkey := loadedConfiguration.Cache.Enabled && loadedConfiguration.Cache.Store == configuration.CacheStoreValkey
	return loadedConfiguration.RateLimit.Enabled || cacheOnValkey
}

// provideValkey registers the one Valkey client (with a health check)
// shared by rate limiting and the cache, and returns its handle. It
// returns nil, registering nothing and never contacting Valkey, when no
// feature needs it.
func provideValkey(instance *application.Application, loadedConfiguration configuration.Configuration) *application.Handle[valkeygo.Client] {
	if !needsValkey(loadedConfiguration) {
		return nil
	}
	settings := valkey.Settings{
		Address:      loadedConfiguration.Valkey.Address,
		Password:     loadedConfiguration.Valkey.Password,
		Database:     loadedConfiguration.Valkey.Database,
		DialTimeout:  loadedConfiguration.Valkey.DialTimeout,
		WriteTimeout: loadedConfiguration.Valkey.WriteTimeout,
	}
	return application.Provide(instance, application.Dependency[valkeygo.Client]{
		Name: valkey.DependencyName,
		Up: func(ctx context.Context) (valkeygo.Client, error) {
			return valkey.Up(ctx, settings)
		},
		Down:  valkey.Down,
		Check: valkey.Check,
	})
}
