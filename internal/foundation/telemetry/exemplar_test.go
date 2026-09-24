package telemetry

import (
	"context"
	"encoding/hex"
	"testing"

	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// TestDefaultExemplarFilterAttachesTraceContext documents and verifies
// that the meter provider built without an explicit WithExemplarFilter
// option uses the SDK's default exemplar.TraceBasedFilter: a
// measurement recorded inside a sampled span is offered as an exemplar
// and carries that span's trace ID, which is what lets a histogram data
// point be linked back to the trace that produced it.
func TestDefaultExemplarFilterAttachesTraceContext(t *testing.T) {
	reader := sdkmetric.NewManualReader()
	meterProvider := sdkmetric.NewMeterProvider(sdkmetric.WithReader(reader))
	tracerProvider := sdktrace.NewTracerProvider(sdktrace.WithSampler(sdktrace.AlwaysSample()))

	meter := meterProvider.Meter("exemplar-test")
	histogram, err := meter.Float64Histogram("test.duration")
	if err != nil {
		t.Fatalf("create histogram: %v", err)
	}

	ctx, span := tracerProvider.Tracer("exemplar-test").Start(context.Background(), "operation")
	histogram.Record(ctx, 1.5)
	span.End()

	var data metricdata.ResourceMetrics
	if err := reader.Collect(context.Background(), &data); err != nil {
		t.Fatalf("collect: %v", err)
	}

	exemplarTraceIDs := findHistogramExemplarTraceIDs(t, data)
	if len(exemplarTraceIDs) == 0 {
		t.Fatalf("expected at least one exemplar carrying a trace ID")
	}
	if exemplarTraceIDs[0] != span.SpanContext().TraceID().String() {
		t.Errorf("exemplar trace ID = %s, want %s", exemplarTraceIDs[0], span.SpanContext().TraceID().String())
	}
}

// findHistogramExemplarTraceIDs collects every exemplar trace ID found
// on any float64 histogram data point in data.
func findHistogramExemplarTraceIDs(t *testing.T, data metricdata.ResourceMetrics) []string {
	t.Helper()

	var traceIDs []string
	for _, scopeMetrics := range data.ScopeMetrics {
		for _, metricValue := range scopeMetrics.Metrics {
			histogram, ok := metricValue.Data.(metricdata.Histogram[float64])
			if !ok {
				continue
			}
			for _, dataPoint := range histogram.DataPoints {
				for _, exemplar := range dataPoint.Exemplars {
					traceIDs = append(traceIDs, hex.EncodeToString(exemplar.TraceID))
				}
			}
		}
	}
	return traceIDs
}
