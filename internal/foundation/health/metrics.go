package health

import (
	"context"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// meter is the global OpenTelemetry meter used to record health check
// metrics, obtained through the OpenTelemetry API only (never through
// internal/foundation/telemetry, so this package stays free of the
// telemetry SDK and foundation packages never import each other).
var meter = otel.Meter("github.com/MathiasHilgert/frappe-api/internal/foundation/health")

// Metric names, prefixed "frappe." to match this application's metric
// naming convention.
const (
	dependencyUpMetricName        = "frappe.dependency.up"
	dependencyCheckDurationMetric = "frappe.dependency.check.duration"
	dependencyAttributeName       = "dependency"
)

// dependencyUp reports 1 while a dependency's check is passing, 0 while
// it is marked failing.
var dependencyUp, _ = meter.Float64Gauge(
	dependencyUpMetricName,
	metric.WithDescription("1 while a dependency's health check is passing, 0 while it is marked failing"),
)

// dependencyCheckDuration records how long each health check run took, in
// seconds.
var dependencyCheckDuration, _ = meter.Float64Histogram(
	dependencyCheckDurationMetric,
	metric.WithUnit("s"),
	metric.WithDescription("Duration of a single dependency health check run"),
)

// recordCheck records the outcome of one check run: its duration always,
// and its up/down gauge value.
func recordCheck(ctx context.Context, name string, duration time.Duration, passing bool) {
	attributes := metric.WithAttributes(attribute.String(dependencyAttributeName, name))

	dependencyCheckDuration.Record(ctx, duration.Seconds(), attributes)

	value := 0.0
	if passing {
		value = 1.0
	}
	dependencyUp.Record(ctx, value, attributes)
}
