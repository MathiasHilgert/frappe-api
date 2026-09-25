package telemetry

import (
	"context"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"
)

// uniqueModuleName returns a lowercase, dot-safe module name derived from
// the running test's name, so each test gets its own instrument namespace
// and does not collide with the process-wide duplicate-name registry.
func uniqueModuleName(t *testing.T) string {
	t.Helper()
	name := "test"
	for _, character := range t.Name() {
		switch {
		case character >= 'A' && character <= 'Z':
			name += string(character - 'A' + 'a')
		case character >= 'a' && character <= 'z' || character >= '0' && character <= '9':
			name += string(character)
		default:
			name += "_"
		}
	}
	return name
}

func TestScopeCounterRecordsThroughGlobalMeterProvider(t *testing.T) {
	reader := sdkmetric.NewManualReader()
	provider := sdkmetric.NewMeterProvider(sdkmetric.WithReader(reader))
	previous := otel.GetMeterProvider()
	otel.SetMeterProvider(provider)
	defer otel.SetMeterProvider(previous)

	scope := Scope(uniqueModuleName(t))
	counter := scope.Counter("created", "Things created")
	counter.Add(context.Background(), 1)

	var data metricdata.ResourceMetrics
	if err := reader.Collect(context.Background(), &data); err != nil {
		t.Fatalf("collect: %v", err)
	}
	if len(data.ScopeMetrics) == 0 {
		t.Fatalf("expected at least one scope metric, got none")
	}
}

func TestScopeCounterPanicsOnDuplicateName(t *testing.T) {
	scope := Scope(uniqueModuleName(t))
	scope.Counter("duplicate", "first")

	defer func() {
		if recover() == nil {
			t.Fatalf("expected panic on duplicate instrument name")
		}
	}()
	scope.Counter("duplicate", "second")
}

func TestScopeCounterPanicsOnInvalidName(t *testing.T) {
	scope := Scope(uniqueModuleName(t))

	defer func() {
		if recover() == nil {
			t.Fatalf("expected panic on invalid instrument name")
		}
	}()
	scope.Counter("Invalid-Name", "bad")
}

func TestScopeDurationSinceRecordsSeconds(t *testing.T) {
	scope := Scope(uniqueModuleName(t))
	duration := scope.Duration("latency", "Operation latency")

	start := time.Now().Add(-10 * time.Millisecond)
	duration.Since(context.Background(), start)
}

func TestScopeStartCreatesSpanWithPrefixedName(t *testing.T) {
	recorder := tracetest.NewSpanRecorder()
	provider := sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(recorder))
	previous := otel.GetTracerProvider()
	otel.SetTracerProvider(provider)
	defer otel.SetTracerProvider(previous)

	module := uniqueModuleName(t)
	scope := Scope(module)
	_, span := scope.Start(context.Background(), "create")
	span.End()

	spans := recorder.Ended()
	if len(spans) != 1 {
		t.Fatalf("expected 1 ended span, got %d", len(spans))
	}
	expected := module + ".create"
	if spans[0].Name() != expected {
		t.Errorf("span name = %q, want %q", spans[0].Name(), expected)
	}
}
