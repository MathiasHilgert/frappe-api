package events_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

var consumerCreated = events.Define[orderCreated]("consumertest.created", 1)

func TestOnRegistersDurableSubscription(t *testing.T) {
	registry := events.NewRegistry()
	events.On(registry.Module("billing"), consumerCreated, func(context.Context, events.Event[orderCreated]) error { return nil })
	events.On(registry.Module("shipping"), consumerCreated,
		func(context.Context, events.Event[orderCreated]) error { return nil },
		events.Named("label"),
		events.WithMaxDeliveries(3),
		events.WithBackoff(time.Second, time.Minute),
	)

	registrations := registry.Registrations()
	if len(registrations) != 2 {
		t.Fatalf("got %d registrations, want 2", len(registrations))
	}
	first := registrations[0].Subscription
	if first.Name != "billing-consumertest-created-v1" || first.Subject != "frappe.consumertest.created.v1" {
		t.Fatalf("first subscription = %+v", first)
	}
	if first.MaxDeliveries != 0 || len(first.Backoff) != 0 {
		t.Fatalf("defaults must be left to the subscriber: %+v", first)
	}
	second := registrations[1].Subscription
	if second.Name != "shipping-label-consumertest-created-v1" || second.MaxDeliveries != 3 || len(second.Backoff) != 2 {
		t.Fatalf("second subscription = %+v", second)
	}
	if registrations[0].Handler == nil {
		t.Fatal("handler must be set")
	}
}

func TestOnPanicsOnMisuse(t *testing.T) {
	registry := events.NewRegistry()
	handler := func(context.Context, events.Event[orderCreated]) error { return nil }

	assertPanics(t, func() { events.On(registry, consumerCreated, handler) })
	assertPanics(t, func() { registry.Module("Not Valid") })
	assertPanics(t, func() { events.On(registry.Module("billing"), consumerCreated, nil) })
	assertPanics(t, func() { events.On(registry.Module("billing"), consumerCreated, handler, events.Named("Bad Name")) })

	events.On(registry.Module("billing"), consumerCreated, handler)
	assertPanics(t, func() { events.On(registry.Module("billing"), consumerCreated, handler) })
}

func TestOnHandlerDecodesAndContinuesTrace(t *testing.T) {
	registry := events.NewRegistry()
	var received events.Event[orderCreated]
	var handlerSpan trace.SpanContext
	events.On(registry.Module("tracing"), consumerCreated, func(ctx context.Context, event events.Event[orderCreated]) error {
		received = event
		handlerSpan = trace.SpanContextFromContext(ctx)
		return nil
	})

	producer := producerContext(t)
	message, err := events.NewMessage(producer, consumerCreated.With(orderCreated{OrderID: "order-9"}).ForEntity("order-9", 1))
	if err != nil {
		t.Fatal(err)
	}

	if err := registry.Registrations()[0].Handler(context.Background(), message); err != nil {
		t.Fatalf("handler: %v", err)
	}
	if received.Data.OrderID != "order-9" || received.Sequence != 1 {
		t.Fatalf("received %+v", received)
	}
	producerSpan := trace.SpanContextFromContext(producer)
	if handlerSpan.TraceID() != producerSpan.TraceID() {
		t.Fatalf("consumer trace %s, want producer trace %s", handlerSpan.TraceID(), producerSpan.TraceID())
	}

	span := findSpan(t, handlerSpan.SpanID())
	if span.Parent().SpanID() != producerSpan.SpanID() || !span.Parent().IsRemote() {
		t.Fatalf("consumer span parent = %v, want remote producer span", span.Parent())
	}
	if span.SpanKind() != trace.SpanKindConsumer || span.Name() != "frappe.consumertest.created.v1 process" {
		t.Fatalf("span kind/name = %v %q", span.SpanKind(), span.Name())
	}
}

func TestOnHandlerOutcomes(t *testing.T) {
	registry := events.NewRegistry()
	failure := errors.New("database down")
	var result error
	events.On(registry.Module("outcomes"), consumerCreated, func(context.Context, events.Event[orderCreated]) error { return result })
	handler := registry.Registrations()[0].Handler
	message, err := events.NewMessage(context.Background(), consumerCreated.With(orderCreated{}))
	if err != nil {
		t.Fatal(err)
	}
	before := handledCount(t, "outcomes", "failure")

	result = failure
	if err := handler(context.Background(), message); !errors.Is(err, failure) || events.IsPermanent(err) {
		t.Fatalf("handler error = %v, want retryable %v", err, failure)
	}
	if got := handledCount(t, "outcomes", "failure"); got != before+1 {
		t.Fatalf("failure count = %d, want %d", got, before+1)
	}

	invalid := message
	invalid.Payload = []byte(`{"specversion":"1.0"}`)
	if err := handler(context.Background(), invalid); !events.IsPermanent(err) || !errors.Is(err, events.ErrInvalidEnvelope) {
		t.Fatalf("undecodable message error = %v, want permanent ErrInvalidEnvelope", err)
	}
	if got := handledCount(t, "outcomes", "invalid"); got != 1 {
		t.Fatalf("invalid count = %d, want 1", got)
	}

	result = nil
	if err := handler(context.Background(), message); err != nil {
		t.Fatalf("handler: %v", err)
	}
	if got := handledCount(t, "outcomes", "success"); got != 1 {
		t.Fatalf("success count = %d, want 1", got)
	}
	if !hasDuration(t, "outcomes") {
		t.Fatal("frappe.events.handle.duration not recorded")
	}
}

func producerContext(t *testing.T) context.Context {
	t.Helper()
	traceID, _ := trace.TraceIDFromHex("4bf92f3577b34da6a3ce929d0e0e4736")
	spanID, _ := trace.SpanIDFromHex("00f067aa0ba902b7")
	return trace.ContextWithSpanContext(context.Background(), trace.NewSpanContext(trace.SpanContextConfig{
		TraceID: traceID, SpanID: spanID, TraceFlags: trace.FlagsSampled, Remote: true,
	}))
}

func findSpan(t *testing.T, spanID trace.SpanID) interface {
	Parent() trace.SpanContext
	SpanKind() trace.SpanKind
	Name() string
} {
	t.Helper()
	for _, span := range spanRecorder.Ended() {
		if span.SpanContext().SpanID() == spanID {
			return span
		}
	}
	t.Fatalf("span %s not recorded", spanID)
	return nil
}

func collect(t *testing.T) metricdata.ResourceMetrics {
	t.Helper()
	var data metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &data); err != nil {
		t.Fatal(err)
	}
	return data
}

// consumerAttribute identifies the durable consumer in the metrics.
func consumerAttribute(module string) attribute.KeyValue {
	return attribute.String("consumer", module+"-consumertest-created-v1")
}

func handledCount(t *testing.T, module, outcome string) int64 {
	t.Helper()
	for _, scope := range collect(t).ScopeMetrics {
		for _, found := range scope.Metrics {
			if found.Name != "frappe.events.handled" {
				continue
			}
			for _, point := range found.Data.(metricdata.Sum[int64]).DataPoints {
				consumer, _ := point.Attributes.Value(consumerAttribute(module).Key)
				got, _ := point.Attributes.Value("outcome")
				eventType, _ := point.Attributes.Value("type")
				if consumer.AsString() == consumerAttribute(module).Value.AsString() && got.AsString() == outcome && eventType.AsString() == "frappe.consumertest.created.v1" {
					return point.Value
				}
			}
		}
	}
	return 0
}

func hasDuration(t *testing.T, module string) bool {
	t.Helper()
	for _, scope := range collect(t).ScopeMetrics {
		for _, found := range scope.Metrics {
			if found.Name != "frappe.events.handle.duration" || found.Unit != "s" {
				continue
			}
			for _, point := range found.Data.(metricdata.Histogram[float64]).DataPoints {
				consumer, _ := point.Attributes.Value(consumerAttribute(module).Key)
				if consumer.AsString() == consumerAttribute(module).Value.AsString() && point.Count > 0 {
					return true
				}
			}
		}
	}
	return false
}
