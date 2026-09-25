package dependencies

import (
	"context"
	"errors"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/nats"
)

// errBrokerNotReady is returned while the NATS client is not up (yet).
var errBrokerNotReady = errors.New("event broker is not ready")

// eventBroker is the Publisher/Subscriber pair selected by EVENTS_BROKER.
// Both are nil when no broker is configured.
type eventBroker struct {
	Publisher  events.Publisher
	Subscriber events.Subscriber
}

// provideEvents selects the broker named by EVENTS_BROKER. For nats it
// registers the NATS client as a dependency with a health check and
// returns adapters that resolve the client once it is up.
func provideEvents(instance *application.Application, loadedConfiguration configuration.Configuration) eventBroker {
	switch loadedConfiguration.Events.Broker {
	case configuration.EventsBrokerMemory:
		broker := memory.NewBroker()
		return eventBroker{Publisher: broker, Subscriber: broker}
	case configuration.EventsBrokerNATS:
		adapter := natsBroker{client: provideNATS(instance, loadedConfiguration.NATS)}
		return eventBroker{Publisher: adapter, Subscriber: adapter}
	default:
		return eventBroker{}
	}
}

// provideNATS registers the NATS client dependency.
func provideNATS(instance *application.Application, settings configuration.NATS) *application.Handle[*nats.Client] {
	natsSettings := nats.Settings{
		URL:                         settings.URL,
		Token:                       settings.Token,
		User:                        settings.User,
		Password:                    settings.Password,
		CredentialsFile:             settings.CredentialsFile,
		TLSCertificateAuthorityFile: settings.TLSCertificateAuthorityFile,
		ConnectTimeout:              settings.ConnectTimeout,
		Replicas:                    settings.StreamReplicas,
		MaxAge:                      settings.StreamMaxAge,
		DuplicateWindow:             settings.StreamDuplicateWindow,
		DeadLetterMaxAge:            settings.DeadLetterMaxAge,
		AckWait:                     settings.AckWait,
	}
	return application.Provide(instance, application.Dependency[*nats.Client]{
		Name: nats.DependencyName,
		Up: func(ctx context.Context) (*nats.Client, error) {
			return nats.Up(ctx, natsSettings)
		},
		Down:  nats.Down,
		Check: nats.Check,
	})
}

// natsBroker adapts the lazily started NATS client to the events ports.
type natsBroker struct {
	client *application.Handle[*nats.Client]
}

func (broker natsBroker) ready() (*nats.Client, error) {
	client, ready := broker.client.Get()
	if !ready || client == nil {
		return nil, errBrokerNotReady
	}
	return client, nil
}

// Publish implements events.Publisher.
func (broker natsBroker) Publish(ctx context.Context, message events.Message) error {
	client, err := broker.ready()
	if err != nil {
		return err
	}
	return client.Publish(ctx, message)
}

// Subscribe implements events.Subscriber.
func (broker natsBroker) Subscribe(ctx context.Context, subscription events.Subscription, handler events.Handler) error {
	client, err := broker.ready()
	if err != nil {
		return err
	}
	return client.Subscribe(ctx, subscription, handler)
}
