package application

import (
	"context"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// meter is the global OpenTelemetry meter used to record lifecycle
// metrics. It is obtained through the OpenTelemetry API only, never
// through internal/foundation/telemetry, so this package stays decoupled
// from the telemetry SDK wiring and works before that SDK's Up has run:
// otel.Meter delegates to a no-op implementation until a real
// MeterProvider is installed.
var meter = otel.Meter("github.com/MathiasHilgert/frappe-api/internal/foundation/application")

// Lifecycle metric names, all prefixed "frappe.application." to match
// this application's metric naming convention.
const (
	hookDurationMetricName = "frappe.application.hook.duration"
	hookFailuresMetricName = "frappe.application.hook.failures"
	readyMetricName        = "frappe.application.ready"
	infoMetricName         = "frappe.application.info"
)

// hookDuration records how long each hook phase took, in seconds.
var hookDuration, _ = meter.Float64Histogram(
	hookDurationMetricName,
	metric.WithUnit("s"),
	metric.WithDescription("Duration of an application hook's Up or Down phase"),
)

// hookFailures counts how many hook phases failed.
var hookFailures, _ = meter.Int64Counter(
	hookFailuresMetricName,
	metric.WithUnit("{count}"),
	metric.WithDescription("Count of application hook phases that returned an error"),
)

// ready reports 1 once every hook's Up has succeeded, and 0 once Down
// starts.
var ready, _ = meter.Float64Gauge(
	readyMetricName,
	metric.WithDescription("1 once every application hook is up, 0 once shutdown starts"),
)

// info reports 1 with the running build's identity as attributes.
var info, _ = meter.Float64Gauge(
	infoMetricName,
	metric.WithDescription("Identity of the running build: 1, with version, commit and environment attributes"),
)

// recordHookPhase records the outcome of one hook phase's duration and,
// on failure, increments the failure counter.
func recordHookPhase(ctx context.Context, name, phase string, duration time.Duration, err error) {
	outcome := "success"
	if err != nil {
		outcome = "failure"
	}

	attributes := metric.WithAttributes(
		attribute.String("hook", name),
		attribute.String("phase", phase),
	)

	hookDuration.Record(ctx, duration.Seconds(), metric.WithAttributes(
		attribute.String("hook", name),
		attribute.String("phase", phase),
		attribute.String("outcome", outcome),
	))

	if err != nil {
		hookFailures.Add(ctx, 1, attributes)
	}
}

// recordReady records whether the application is fully up (value 1) or
// shutting down (value 0).
func recordReady(ctx context.Context, value float64) {
	ready.Record(ctx, value)
}

// recordBuildInfo records the running build's identity once.
func recordBuildInfo(ctx context.Context, version, commit, environment string) {
	info.Record(ctx, 1, metric.WithAttributes(
		attribute.String("version", version),
		attribute.String("commit", commit),
		attribute.String("environment", environment),
	))
}
