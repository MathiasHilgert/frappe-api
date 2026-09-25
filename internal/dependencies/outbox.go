package dependencies

import (
	"context"
	"errors"
	"fmt"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/postgres"
)

// outboxDependencyName identifies the outbox relay dependency.
const outboxDependencyName = "outbox"

// ErrOutboxPublisherRequired is returned by NewApplication when
// OUTBOX_ENABLED is true but no publisher is available: EVENTS_BROKER is
// none (configuration.Validate reports that too) and none was given with
// WithPublisher. The relay would have nowhere to publish.
var ErrOutboxPublisherRequired = errors.New("outbox: OUTBOX_ENABLED=true requires EVENTS_BROKER other than none")

// Option customizes NewApplication.
type Option func(*options)

// options collects what the entrypoint hands the composition root.
type options struct {
	publisher events.Publisher
}

// WithPublisher overrides the publisher the outbox relay publishes to,
// which defaults to the broker selected by EVENTS_BROKER. Tests use it to
// observe what the relay publishes.
func WithPublisher(publisher events.Publisher) Option {
	return func(resolved *options) { resolved.publisher = publisher }
}

// outboxRuntime is what the outbox dependency starts and stops.
type outboxRuntime struct {
	pool  *pgxpool.Pool
	store *postgres.Store
	relay *outbox.Relay
}

// provideOutbox returns the outbox Recorder modules record events with and,
// when OUTBOX_ENABLED is true, registers the relay as a dependency. The
// Recorder always appends to the Postgres outbox inside the caller's
// business transaction, so events recorded while the relay is disabled are
// kept and published once it is enabled. The relay gets its own pool as
// frappe_outbox_relay (DATABASE_OUTBOX_RELAY_URL), the only role allowed to
// read the outbox, and a health check pinging that pool.
func provideOutbox(instance *application.Application, loadedConfiguration configuration.Configuration, publisher events.Publisher) (*outbox.Recorder, error) {
	// Stores are handed to outbox as the outbox.Store port, never as the
	// concrete type: go-arch-lint's deep scan treats injecting a concrete
	// adapter as the receiving component depending on it.
	var appendOnlyStore outbox.Store = postgres.NewStore(nil)
	recorder := outbox.NewRecorder(appendOnlyStore)
	if !loadedConfiguration.Outbox.Enabled {
		return recorder, nil
	}
	if publisher == nil {
		return nil, ErrOutboxPublisherRequired
	}

	poolSettings := database.Settings{
		URL:                   loadedConfiguration.Database.OutboxRelayURL,
		MaxConnections:        loadedConfiguration.Database.MaxConnections,
		MinConnections:        loadedConfiguration.Database.MinConnections,
		MaxConnectionLifetime: loadedConfiguration.Database.MaxConnectionLifetime,
		MaxConnectionIdleTime: loadedConfiguration.Database.MaxConnectionIdleTime,
		ConnectTimeout:        loadedConfiguration.Database.ConnectTimeout,
	}
	relaySettings := outbox.Settings{
		BatchSize:     loadedConfiguration.Outbox.BatchSize,
		Lease:         loadedConfiguration.Outbox.Lease,
		PollInterval:  loadedConfiguration.Outbox.PollInterval,
		PurgeInterval: loadedConfiguration.Outbox.PurgeInterval,
		Retention:     loadedConfiguration.Outbox.Retention,
		BaseBackoff:   loadedConfiguration.Outbox.BaseBackoff,
		MaxBackoff:    loadedConfiguration.Outbox.MaxBackoff,
	}
	application.Provide(instance, application.Dependency[*outboxRuntime]{
		Name: outboxDependencyName,
		Up: func(ctx context.Context) (*outboxRuntime, error) {
			return startOutbox(ctx, poolSettings, relaySettings, publisher)
		},
		Down: stopOutbox,
		Check: func(ctx context.Context, runtime *outboxRuntime) error {
			return database.Check(ctx, runtime.pool)
		},
	})
	return recorder, nil
}

// startOutbox opens the relay pool, starts the store's listener, then the
// relay, undoing what already started if a later step fails.
func startOutbox(ctx context.Context, poolSettings database.Settings, relaySettings outbox.Settings, publisher events.Publisher) (*outboxRuntime, error) {
	pool, err := database.Up(ctx, poolSettings)
	if err != nil {
		return nil, fmt.Errorf("outbox relay pool: %w", err)
	}
	store := postgres.NewStore(pool)
	if err := store.Start(ctx); err != nil {
		pool.Close()
		return nil, err
	}
	var relayStore outbox.Store = store
	relay := outbox.NewRelay(relayStore, publisher, relaySettings)
	if err := relay.Start(ctx); err != nil {
		_ = store.Stop(ctx)
		pool.Close()
		return nil, err
	}
	return &outboxRuntime{pool: pool, store: store, relay: relay}, nil
}

// stopOutbox stops the relay first, so nothing claims while the listener
// and pool close.
func stopOutbox(ctx context.Context, runtime *outboxRuntime) error {
	relayError := runtime.relay.Stop(ctx)
	storeError := runtime.store.Stop(ctx)
	runtime.pool.Close()
	return errors.Join(relayError, storeError)
}
