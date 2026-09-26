package usecase_test

import (
	"bytes"
	"context"
	"errors"
	"log/slog"
	"os"
	"strings"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
)

// Global telemetry providers installed once for the whole package, so the
// instruments usecase creates through the global OpenTelemetry API are
// observable in tests.
var (
	spanRecorder = tracetest.NewSpanRecorder()
	metricReader = sdkmetric.NewManualReader()
)

func TestMain(m *testing.M) {
	otel.SetTracerProvider(sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(spanRecorder)))
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	os.Exit(m.Run())
}

var errMissing = errors.New("thing: not found")

// GetThingHandler, FindThingHandler and LoadThingHandler are distinct
// handler types, so each test observes its own span and metric series.
type GetThingHandler struct{}

func (GetThingHandler) Handle(_ context.Context, name string) (int, error) { return len(name), nil }

type FindThingHandler struct{}

func (FindThingHandler) Handle(context.Context, string) (int, error) { return 0, errMissing }

type LoadThingHandler struct{}

func (LoadThingHandler) Handle(context.Context, string) (int, error) {
	return 0, errors.New("connection refused")
}

// probe reads what the global providers recorded.
type probe struct{ t *testing.T }

func (probe probe) span(name string) sdktrace.ReadOnlySpan {
	probe.t.Helper()
	for _, span := range spanRecorder.Ended() {
		if span.Name() == name {
			return span
		}
	}
	probe.t.Fatalf("no span %q was ended", name)
	return nil
}

// calls returns the frappe.usecase.calls count of handler with outcome,
// and whether frappe.usecase.duration recorded the same series.
func (probe probe) calls(handler, outcome string) (int64, bool) {
	probe.t.Helper()
	var metrics metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &metrics); err != nil {
		probe.t.Fatalf("collect metrics: %v", err)
	}
	want := attribute.NewSet(attribute.String("handler", handler), attribute.String("outcome", outcome))
	var count int64
	var timed bool
	for _, scope := range metrics.ScopeMetrics {
		for _, instrument := range scope.Metrics {
			switch data := instrument.Data.(type) {
			case metricdata.Sum[int64]:
				for _, point := range data.DataPoints {
					if instrument.Name == "frappe.usecase.calls" && point.Attributes.Equals(&want) {
						count += point.Value
					}
				}
			case metricdata.Histogram[float64]:
				for _, point := range data.DataPoints {
					if instrument.Name == "frappe.usecase.duration" && point.Attributes.Equals(&want) {
						timed = timed || point.Count > 0
					}
				}
			}
		}
	}
	return count, timed
}

func TestObservedSpansCountsAndTimesASuccessfulCall(t *testing.T) {
	handler := usecase.NewObserved[string, int](GetThingHandler{})
	if handler.Name() != "usecase_test.get_thing" {
		t.Fatalf("Name = %q, want usecase_test.get_thing", handler.Name())
	}

	result, err := handler.Handle(context.Background(), "four")
	if err != nil || result != 4 {
		t.Fatalf("Handle = %d, %v; want the wrapped handler's result", result, err)
	}
	if status := (probe{t}).span("usecase_test.get_thing").Status(); status.Code != codes.Unset {
		t.Errorf("span status = %v, want unset", status)
	}
	if count, timed := (probe{t}).calls("usecase_test.get_thing", "success"); count != 1 || !timed {
		t.Errorf("success calls = %d (timed %v), want 1 timed call", count, timed)
	}
}

func TestObservedTreatsExpectedErrorsAsRejections(t *testing.T) {
	handler := usecase.NewObserved[string, int](FindThingHandler{}, errMissing)

	if _, err := handler.Handle(context.Background(), "x"); !errors.Is(err, errMissing) {
		t.Fatalf("Handle error = %v, want the expected error unchanged", err)
	}
	if status := (probe{t}).span("usecase_test.find_thing").Status(); status.Code == codes.Error {
		t.Errorf("an expected error marked the span as failed: %v", status)
	}
	if count, _ := (probe{t}).calls("usecase_test.find_thing", "rejected"); count != 1 {
		t.Errorf("rejected calls = %d, want 1", count)
	}
}

func TestObservedRecordsAndLogsUnexpectedErrors(t *testing.T) {
	var logs bytes.Buffer
	previous := slog.Default()
	slog.SetDefault(slog.New(slog.NewJSONHandler(&logs, nil)))
	t.Cleanup(func() { slog.SetDefault(previous) })
	handler := usecase.NewObserved[string, int](&LoadThingHandler{})

	if _, err := handler.Handle(context.Background(), "x"); err == nil {
		t.Fatalf("Handle error = nil, want the wrapped handler's error")
	}
	span := (probe{t}).span("usecase_test.load_thing")
	if span.Status().Code != codes.Error || len(span.Events()) == 0 {
		t.Errorf("span status %v with %d events, want an error with the recorded exception", span.Status(), len(span.Events()))
	}
	if count, _ := (probe{t}).calls("usecase_test.load_thing", "error"); count != 1 {
		t.Errorf("error calls = %d, want 1", count)
	}
	if line := logs.String(); !strings.Contains(line, `"level":"ERROR"`) || !strings.Contains(line, `"handler":"usecase_test.load_thing"`) {
		t.Errorf("log = %q, want an ERROR record naming the handler", line)
	}
}
