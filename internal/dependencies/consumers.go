package dependencies

import (
	"context"
	"errors"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/postgres"
)

// consumersDependencyName identifies the consumer runtime dependency.
const consumersDependencyName = "consumers"

// errDatabaseNotReady is returned while the application pool is not up.
var errDatabaseNotReady = errors.New("database pool is not ready")

// provideConsumers registers the consumer runtime when a broker is selected
// and reports whether it did. At Up it subscribes every subscription modules
// added to registry on the broker's Subscriber, deduplicating each handler
// through the inbox store returned by newStore; at Down it cancels them and
// waits for in-flight handlers.
func provideConsumers(lifecycle application.Lifecycle, settings configuration.Inbox, broker eventBroker, registry *events.Registry, newStore func() (inbox.Store, error)) bool {
	if broker.Subscriber == nil {
		return false
	}
	inboxSettings := inbox.Settings{PurgeInterval: settings.PurgeInterval, Retention: settings.Retention}
	application.Provide(lifecycle, application.Dependency[*inbox.Consumers]{
		Name: consumersDependencyName,
		Up: func(ctx context.Context) (*inbox.Consumers, error) {
			store, err := newStore()
			if err != nil {
				return nil, err
			}
			consumers := inbox.NewConsumers(registry, broker.Subscriber, store, inboxSettings)
			if err := consumers.Start(ctx); err != nil {
				return nil, err
			}
			return consumers, nil
		},
		Down: func(ctx context.Context, consumers *inbox.Consumers) error {
			return consumers.Stop(ctx)
		},
	})
	return true
}

// postgresInbox returns a resolver of the Postgres inbox store on the
// application pool, available once that pool is up.
func postgresInbox(pool *application.Handle[*pgxpool.Pool]) func() (inbox.Store, error) {
	return func() (inbox.Store, error) {
		connected, ready := pool.Get()
		if !ready || connected == nil {
			return nil, errDatabaseNotReady
		}
		return postgres.NewStore(connected), nil
	}
}

// resolveOutboxPublisher returns the publisher the outbox relay publishes
// to: the one given with WithPublisher, else the broker's.
func resolveOutboxPublisher(override events.Publisher, broker eventBroker) events.Publisher {
	if override != nil {
		return override
	}
	return broker.Publisher
}
