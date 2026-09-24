package telemetry

import (
	"context"
	"time"

	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// Counter wraps a monotonic int64 counter instrument, exposing the native
// OpenTelemetry Add method.
type Counter struct {
	metric.Int64Counter
}

// UpDownCounter wraps a non-monotonic int64 counter instrument, exposing
// the native OpenTelemetry Add method.
type UpDownCounter struct {
	metric.Int64UpDownCounter
}

// Histogram wraps a float64 histogram instrument, exposing the native
// OpenTelemetry Record method.
type Histogram struct {
	metric.Float64Histogram
}

// Gauge wraps a synchronous float64 gauge instrument, exposing the native
// OpenTelemetry Record method.
type Gauge struct {
	metric.Float64Gauge
}

// Duration wraps a float64 histogram instrument recorded in seconds,
// adding the Since convenience method on top of the native OpenTelemetry
// Record method.
type Duration struct {
	metric.Float64Histogram
}

// Since records the elapsed time since start, in seconds, with the given
// attributes.
func (duration Duration) Since(ctx context.Context, start time.Time, attributes ...attribute.KeyValue) {
	duration.Record(ctx, time.Since(start).Seconds(), metric.WithAttributes(attributes...))
}
