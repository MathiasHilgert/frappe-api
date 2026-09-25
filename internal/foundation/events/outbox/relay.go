package outbox

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

// instrumentationScope names the meter used by the relay.
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"

// Settings tune a Relay. Zero values fall back to the defaults documented on
// each field.
type Settings struct {
	// BatchSize is the maximum number of messages claimed at once
	// (default 100).
	BatchSize int
	// Lease is how long a claimed batch stays invisible to other relay
	// replicas (default 30s). It must exceed the time needed to publish a
	// batch, otherwise messages are published twice.
	Lease time.Duration
	// PollInterval is how often the relay looks for pending messages when
	// no notification arrives (default 1s).
	PollInterval time.Duration
	// PurgeInterval is how often published messages are purged (default 1h).
	PurgeInterval time.Duration
	// Retention is how long published messages are kept before being purged
	// (default 72h).
	Retention time.Duration
	// BaseBackoff is the delay before retrying a message that failed once;
	// it doubles with every further failure (default 1s).
	BaseBackoff time.Duration
	// MaxBackoff caps the retry delay (default 5m).
	MaxBackoff time.Duration
}

// withDefaults returns settings with every zero value replaced by its default.
func (settings Settings) withDefaults() Settings {
	settings.BatchSize = defaultValue(settings.BatchSize, 100)
	settings.Lease = defaultValue(settings.Lease, 30*time.Second)
	settings.PollInterval = defaultValue(settings.PollInterval, time.Second)
	settings.PurgeInterval = defaultValue(settings.PurgeInterval, time.Hour)
	settings.Retention = defaultValue(settings.Retention, 72*time.Hour)
	settings.BaseBackoff = defaultValue(settings.BaseBackoff, time.Second)
	settings.MaxBackoff = defaultValue(settings.MaxBackoff, 5*time.Minute)
	return settings
}

func defaultValue[T int | time.Duration](value, fallback T) T {
	if value <= 0 {
		return fallback
	}
	return value
}

// Backoff returns the retry delay for a message that already failed attempts
// times: BaseBackoff * 2^attempts, capped at MaxBackoff.
func (settings Settings) Backoff(attempts int) time.Duration {
	settings = settings.withDefaults()
	delay := settings.BaseBackoff
	for range attempts {
		if delay >= settings.MaxBackoff/2 {
			return settings.MaxBackoff
		}
		delay *= 2
	}
	return min(delay, settings.MaxBackoff)
}

// relayInstruments are created lazily through the global OpenTelemetry API.
var relayInstruments = sync.OnceValue(func() instruments {
	meter := otel.Meter(instrumentationScope)
	published, err := meter.Int64Counter("frappe.outbox.published",
		metric.WithDescription("Outbox messages published to the broker, by subject."), metric.WithUnit("{message}"))
	if err != nil {
		panic(fmt.Errorf("outbox: create published counter: %w", err))
	}
	failed, err := meter.Int64Counter("frappe.outbox.failed",
		metric.WithDescription("Outbox publish attempts that failed and were scheduled for retry, by subject."), metric.WithUnit("{message}"))
	if err != nil {
		panic(fmt.Errorf("outbox: create failed counter: %w", err))
	}
	lag, err := meter.Float64Gauge("frappe.outbox.lag",
		metric.WithDescription("Age of the oldest unpublished outbox message; zero when the outbox is drained."), metric.WithUnit("s"))
	if err != nil {
		panic(fmt.Errorf("outbox: create lag gauge: %w", err))
	}
	return instruments{published: published, failed: failed, lag: lag}
})

type instruments struct {
	published metric.Int64Counter
	failed    metric.Int64Counter
	lag       metric.Float64Gauge
}

// Relay moves messages from a Store to an events.Publisher. It is storage-
// and broker-agnostic and safe to run on several replicas at once: claims are
// exclusive, and a message whose relay crashed is claimed again once its lease
// expires, so publishing is at-least-once. Every message is published with
// its event ID so brokers that support it can deduplicate.
type Relay struct {
	store     Store
	publisher events.Publisher
	cancel    context.CancelFunc
	done      chan struct{}
	settings  Settings
	mutex     sync.Mutex
}

// NewRelay returns a stopped relay.
func NewRelay(store Store, publisher events.Publisher, settings Settings) *Relay {
	return &Relay{store: store, publisher: publisher, settings: settings.withDefaults()}
}

// RunOnce publishes every currently pending message, batch by batch, and
// returns how many were published. Messages whose publish fails are marked
// failed with an exponential backoff and are not retried within this call.
func (relay *Relay) RunOnce(ctx context.Context) (int, error) {
	total := 0
	for {
		batch, err := relay.store.Claim(ctx, relay.settings.BatchSize, relay.settings.Lease)
		if err != nil {
			return total, fmt.Errorf("outbox: claim: %w", err)
		}
		published, err := relay.publish(ctx, batch)
		total += published
		if err != nil {
			return total, err
		}
		if len(batch) < relay.settings.BatchSize {
			break
		}
	}
	relay.reportLag(ctx)
	return total, nil
}

// publish publishes one claimed batch and settles every message in it.
func (relay *Relay) publish(ctx context.Context, batch []Pending) (int, error) {
	telemetry := relayInstruments()
	published := make([]string, 0, len(batch))
	for _, pending := range batch {
		subject := metric.WithAttributes(attribute.String("subject", pending.Message.Subject))
		if err := relay.publisher.Publish(ctx, pending.Message); err != nil {
			telemetry.failed.Add(ctx, 1, subject)
			retryAt := time.Now().Add(relay.settings.Backoff(pending.Attempts))
			if markErr := relay.store.MarkFailed(ctx, pending.Message.ID, err.Error(), retryAt); markErr != nil {
				return 0, fmt.Errorf("outbox: mark %s failed: %w", pending.Message.ID, markErr)
			}
			continue
		}
		telemetry.published.Add(ctx, 1, subject)
		published = append(published, pending.Message.ID)
	}
	if len(published) == 0 {
		return 0, nil
	}
	if err := relay.store.MarkPublished(ctx, published...); err != nil {
		return 0, fmt.Errorf("outbox: mark published: %w", err)
	}
	return len(published), nil
}

// reportLag records the frappe.outbox.lag gauge when the store supports it.
func (relay *Relay) reportLag(ctx context.Context) {
	reporter, ok := relay.store.(LagReporter)
	if !ok {
		return
	}
	oldest, found, err := reporter.OldestPending(ctx)
	if err != nil {
		slog.WarnContext(ctx, "outbox: read oldest pending message", slog.Any("error", err))
		return
	}
	lag := 0.0
	if found {
		lag = time.Since(oldest).Seconds()
	}
	relayInstruments().lag.Record(ctx, lag)
}

// Purge deletes messages published longer than Retention ago.
func (relay *Relay) Purge(ctx context.Context) (int, error) {
	return relay.store.Purge(ctx, time.Now().Add(-relay.settings.Retention))
}

// Start runs the relay in the background until Stop: it publishes pending
// messages whenever the store notifies an append or PollInterval elapses, and
// purges old published messages every PurgeInterval. Start matches the Up of
// an application lifecycle hook; its ctx only bounds startup.
func (relay *Relay) Start(context.Context) error {
	relay.mutex.Lock()
	defer relay.mutex.Unlock()
	if relay.cancel != nil {
		return errors.New("outbox: relay already started")
	}
	ctx, cancel := context.WithCancel(context.Background())
	relay.cancel, relay.done = cancel, make(chan struct{})
	go relay.loop(ctx, relay.done)
	return nil
}

// Stop stops the background loop and waits for the batch in flight, or until
// ctx is done. Stopping a relay that is not running is a no-op. Stop matches
// the Down of an application lifecycle hook.
func (relay *Relay) Stop(ctx context.Context) error {
	relay.mutex.Lock()
	cancel, done := relay.cancel, relay.done
	relay.cancel, relay.done = nil, nil
	relay.mutex.Unlock()
	if cancel == nil {
		return nil
	}
	cancel()
	select {
	case <-done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

func (relay *Relay) loop(ctx context.Context, done chan<- struct{}) {
	defer close(done)
	poll := time.NewTicker(relay.settings.PollInterval)
	defer poll.Stop()
	purge := time.NewTicker(relay.settings.PurgeInterval)
	defer purge.Stop()
	notifications := relay.store.Notifications()
	for {
		if _, err := relay.RunOnce(ctx); err != nil && ctx.Err() == nil {
			slog.ErrorContext(ctx, "outbox: relay cycle failed", slog.Any("error", err))
		}
		select {
		case <-ctx.Done():
			return
		case <-notifications:
		case <-poll.C:
		case <-purge.C:
			if _, err := relay.Purge(ctx); err != nil && ctx.Err() == nil {
				slog.ErrorContext(ctx, "outbox: purge failed", slog.Any("error", err))
			}
		}
	}
}
