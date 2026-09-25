package inbox

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// instrumentationScope names the meter used by Consumers.
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"

// ErrStopped is returned for a delivery that arrives after Stop; the broker
// redelivers it to another consumer with the same name.
var ErrStopped = errors.New("inbox: consumers are stopped")

// duplicatesCounter counts deliveries skipped as duplicates. It is created
// lazily through the global OpenTelemetry API.
var duplicatesCounter = sync.OnceValue(func() metric.Int64Counter {
	counter, err := otel.Meter(instrumentationScope).Int64Counter("frappe.events.duplicates",
		metric.WithDescription("Deliveries skipped by the inbox because the consumer already processed the event, by type and consumer."),
		metric.WithUnit("{event}"))
	if err != nil {
		panic(fmt.Errorf("inbox: create duplicates counter: %w", err))
	}
	return counter
})

// Settings configures Consumers.
type Settings struct {
	// PurgeInterval is how often records older than Retention are purged.
	PurgeInterval time.Duration
	// Retention is how long processed records are kept. It must outlive
	// every possible redelivery of an event (broker retention, outbox
	// republishing), or a late duplicate is handled again.
	Retention time.Duration
}

// Consumers is the consumer runtime: Start subscribes every registration of
// a Registry on a Subscriber, wrapping each handler with a Store so it runs
// at most once per event ID and durable consumer name, and purges old
// records periodically; Stop cancels the subscriptions and waits for
// in-flight handlers and the purger.
type Consumers struct {
	subscriber events.Subscriber
	store      Store
	cancel     context.CancelFunc
	done       chan struct{}
	registry   *events.Registry
	settings   Settings
	inFlight   sync.WaitGroup
	mutex      sync.Mutex
	stopped    bool
}

// NewConsumers returns a runtime consuming registry's subscriptions from
// subscriber with deduplication by store.
func NewConsumers(registry *events.Registry, subscriber events.Subscriber, store Store, settings Settings) *Consumers {
	return &Consumers{registry: registry, subscriber: subscriber, store: store, settings: settings}
}

// Start subscribes every registration. The subscriptions outlive ctx (which
// only bounds starting) until Stop. If any subscription fails, the ones
// already made are cancelled and the error is returned.
func (consumers *Consumers) Start(ctx context.Context) error {
	running, cancel := context.WithCancel(context.WithoutCancel(ctx))
	for _, registration := range consumers.registry.Registrations() {
		handler := consumers.deduplicated(registration)
		if err := consumers.subscriber.Subscribe(running, registration.Subscription, handler); err != nil {
			cancel()
			return fmt.Errorf("inbox: subscribe %s: %w", registration.Subscription.Name, err)
		}
	}
	consumers.cancel = cancel
	consumers.done = make(chan struct{})
	go consumers.purge(running)
	return nil
}

// Stop cancels every subscription, then waits for in-flight handlers and
// the purger until ctx ends.
func (consumers *Consumers) Stop(ctx context.Context) error {
	consumers.mutex.Lock()
	consumers.stopped = true
	consumers.mutex.Unlock()
	if consumers.cancel == nil {
		return nil
	}
	consumers.cancel()
	finished := make(chan struct{})
	go func() {
		consumers.inFlight.Wait()
		<-consumers.done
		close(finished)
	}()
	select {
	case <-finished:
		return nil
	case <-ctx.Done():
		return fmt.Errorf("inbox: stop: %w", ctx.Err())
	}
}

// deduplicated wraps registration's handler with the store.
func (consumers *Consumers) deduplicated(registration events.Registration) events.Handler {
	consumer := registration.Subscription.Name
	return func(ctx context.Context, message events.Message) error {
		if !consumers.enter() {
			return ErrStopped
		}
		defer consumers.inFlight.Done()

		handled := false
		err := consumers.store.Process(ctx, consumer, message.ID, func(ctx context.Context) error {
			handled = true
			return registration.Handler(ctx, message)
		})
		if err == nil && !handled {
			duplicatesCounter().Add(ctx, 1, metric.WithAttributes(
				attribute.String("type", message.Subject),
				attribute.String("consumer", consumer),
			))
		}
		return err
	}
}

// enter registers an in-flight handler unless Stop was called.
func (consumers *Consumers) enter() bool {
	consumers.mutex.Lock()
	defer consumers.mutex.Unlock()
	if consumers.stopped {
		return false
	}
	consumers.inFlight.Add(1)
	return true
}

// purge deletes old records every PurgeInterval until ctx is cancelled.
func (consumers *Consumers) purge(ctx context.Context) {
	defer close(consumers.done)
	ticker := time.NewTicker(consumers.settings.PurgeInterval)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			if _, err := consumers.store.Purge(ctx, time.Now().Add(-consumers.settings.Retention)); err != nil && ctx.Err() == nil {
				slog.WarnContext(ctx, "inbox: purge", slog.Any("error", err))
			}
		}
	}
}
