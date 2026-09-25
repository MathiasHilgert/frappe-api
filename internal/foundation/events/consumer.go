package events

import (
	"context"
	"fmt"
	"strings"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
)

// instrumentationScope names the tracer and meter used by consumers.
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/events"

// Outcomes recorded on frappe.events.handled and frappe.events.handle.duration.
const (
	outcomeSuccess = "success"
	outcomeFailure = "failure"
	outcomeInvalid = "invalid"
)

// consumerTelemetry holds the tracer and instruments shared by every handler
// built by On. They are created lazily through the global OpenTelemetry API,
// so they follow whatever providers the telemetry foundation installs.
var consumerTelemetry = sync.OnceValue(func() consumerInstruments {
	meter := otel.Meter(instrumentationScope)
	handled, err := meter.Int64Counter("frappe.events.handled",
		metric.WithDescription("Events handled by consumers, by type, consumer and outcome."),
		metric.WithUnit("{event}"))
	if err != nil {
		panic(fmt.Errorf("events: create handled counter: %w", err))
	}
	duration, err := meter.Float64Histogram("frappe.events.handle.duration",
		metric.WithDescription("Duration of event handlers, by type, consumer and outcome."),
		metric.WithUnit("s"))
	if err != nil {
		panic(fmt.Errorf("events: create handle duration histogram: %w", err))
	}
	return consumerInstruments{tracer: otel.Tracer(instrumentationScope), handled: handled, duration: duration}
})

type consumerInstruments struct {
	tracer   trace.Tracer
	handled  metric.Int64Counter
	duration metric.Float64Histogram
}

// Registration is one handler attached to one subscription.
type Registration struct {
	Handler      Handler
	Subscription Subscription
}

// Registry collects the subscriptions of every module. The composition root
// creates one Registry, hands registry.Module("<module>") to each module's
// Subscriptions function, and later subscribes every Registration on the
// broker. It is safe for concurrent use.
type Registry struct {
	shared *registrations
	module string
}

type registrations struct {
	names map[string]struct{}
	list  []Registration
	mutex sync.Mutex
}

// NewRegistry returns an empty Registry.
func NewRegistry() *Registry {
	return &Registry{shared: &registrations{names: map[string]struct{}{}}}
}

// Module returns a view of the registry for the consuming module name; On
// requires such a view to derive durable consumer names. It panics when name
// is not a lowercase identifier.
func (registry *Registry) Module(name string) *Registry {
	if !segmentPattern.MatchString(name) {
		panic(fmt.Sprintf("events: invalid consumer module name %q: must match %s", name, segmentPattern.String()))
	}
	return &Registry{shared: registry.shared, module: name}
}

// Registrations returns every registration in registration order.
func (registry *Registry) Registrations() []Registration {
	registry.shared.mutex.Lock()
	defer registry.shared.mutex.Unlock()
	return append([]Registration(nil), registry.shared.list...)
}

func (registry *Registry) add(registration Registration) {
	registry.shared.mutex.Lock()
	defer registry.shared.mutex.Unlock()
	name := registration.Subscription.Name
	if _, exists := registry.shared.names[name]; exists {
		panic(fmt.Sprintf("events: duplicate subscription %q; use events.Named to register a second handler", name))
	}
	registry.shared.names[name] = struct{}{}
	registry.shared.list = append(registry.shared.list, registration)
}

// Option customizes a subscription registered with On.
type Option func(*Subscription)

// Named distinguishes several handlers of the same module for the same event
// by adding name to the durable consumer name.
func Named(name string) Option {
	if !segmentPattern.MatchString(name) {
		panic(fmt.Sprintf("events: invalid handler name %q: must match %s", name, segmentPattern.String()))
	}
	return func(subscription *Subscription) {
		subscription.Name = strings.Replace(subscription.Name, "-", "-"+name+"-", 1)
	}
}

// WithMaxDeliveries sets Subscription.MaxDeliveries.
func WithMaxDeliveries(maximum int) Option {
	return func(subscription *Subscription) {
		subscription.MaxDeliveries = maximum
	}
}

// WithBackoff sets Subscription.Backoff.
func WithBackoff(delays ...time.Duration) Option {
	return func(subscription *Subscription) {
		subscription.Backoff = delays
	}
}

// On registers handler for every event of definition on registry, which must
// be a module view (registry.Module). The durable consumer is named
// "<module>-<event type without the frappe prefix, dots as dashes>", e.g.
// billing-orders-created-v1.
//
// The registered Handler decodes the envelope, continues the producer's
// trace in a consumer span, calls handler and records the
// frappe.events.handled counter and frappe.events.handle.duration histogram.
// A message that cannot be decoded is rejected with a Permanent error so it
// is dead lettered instead of retried. handler must be idempotent: the same
// event can be delivered more than once.
func On[T any](registry *Registry, definition Definition[T], handler func(ctx context.Context, event Event[T]) error, options ...Option) {
	if registry.module == "" {
		panic("events: On requires a module registry; use registry.Module(\"<module>\")")
	}
	if handler == nil {
		panic("events: On requires a handler")
	}
	subscription := Subscription{
		Name:    registry.module + "-" + strings.ReplaceAll(strings.TrimPrefix(definition.eventType, typePrefix), ".", "-"),
		Subject: definition.eventType,
	}
	for _, option := range options {
		option(&subscription)
	}
	registry.add(Registration{Subscription: subscription, Handler: typedHandler(subscription.Name, definition, handler)})
}

// typedHandler adapts a typed handler to a Handler with tracing and metrics.
func typedHandler[T any](consumer string, definition Definition[T], handler func(context.Context, Event[T]) error) Handler {
	return func(ctx context.Context, message Message) error {
		instruments := consumerTelemetry()
		start := time.Now()
		event, decodeErr := definition.Decode(message.Payload)

		carrier := propagation.MapCarrier{HeaderTraceParent: event.TraceParent, HeaderTraceState: event.TraceState}
		ctx = propagation.TraceContext{}.Extract(ctx, carrier)
		ctx, span := instruments.tracer.Start(ctx, definition.eventType+" process",
			trace.WithSpanKind(trace.SpanKindConsumer),
			trace.WithAttributes(
				attribute.String("messaging.operation.type", "process"),
				attribute.String("messaging.destination.name", message.Subject),
				attribute.String("messaging.consumer.group.name", consumer),
				attribute.String("messaging.message.id", message.ID),
			))
		defer span.End()

		outcome, err := outcomeSuccess, decodeErr
		switch {
		case decodeErr != nil:
			outcome, err = outcomeInvalid, Permanent(decodeErr)
		default:
			if err = handler(ctx, event); err != nil {
				outcome = outcomeFailure
			}
		}
		if err != nil {
			span.RecordError(err)
			span.SetStatus(codes.Error, err.Error())
		}

		attributes := metric.WithAttributes(
			attribute.String("type", definition.eventType),
			attribute.String("consumer", consumer),
			attribute.String("outcome", outcome),
		)
		instruments.handled.Add(ctx, 1, attributes)
		instruments.duration.Record(ctx, time.Since(start).Seconds(), attributes)
		return err
	}
}
