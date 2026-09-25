package jobs_test

import (
	"os"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/propagation"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"
)

// Global telemetry providers installed once for the whole package so that
// the instruments jobs creates through the global OpenTelemetry API are
// observable in tests.
var (
	spanRecorder   = tracetest.NewSpanRecorder()
	metricReader   = sdkmetric.NewManualReader()
	tracerProvider = sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(spanRecorder))
)

func TestMain(m *testing.M) {
	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	otel.SetTextMapPropagator(propagation.TraceContext{})
	os.Exit(m.Run())
}
