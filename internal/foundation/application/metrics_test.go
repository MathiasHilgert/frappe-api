package application_test

import (
	"context"
	"errors"
	"testing"

	"go.opentelemetry.io/otel"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// collectMetricNames returns the set of instrument names recorded on
// reader, so tests can assert that a given metric was emitted without
// depending on exact data-point values.
func collectMetricNames(t *testing.T, reader sdkmetric.Reader) map[string]bool {
	t.Helper()

	var data metricdata.ResourceMetrics
	if err := reader.Collect(context.Background(), &data); err != nil {
		t.Fatalf("collect: %v", err)
	}

	names := map[string]bool{}
	for _, scopeMetrics := range data.ScopeMetrics {
		for _, metricValue := range scopeMetrics.Metrics {
			names[metricValue.Name] = true
		}
	}
	return names
}

// TestApplicationLifecycleEmitsMetrics exercises the whole lifecycle
// metrics surface (hook duration, hook failures, ready and build info)
// through a single global MeterProvider installation: the OpenTelemetry
// global meter binds its delegated instruments to the first
// MeterProvider it sees for the lifetime of the process, so later tests
// that install a second provider would not observe data recorded through
// application's package-level instruments. Exercising every scenario
// under one installation keeps the test faithful to how the SDK is
// actually installed once in main.go.
func TestApplicationLifecycleEmitsMetrics(t *testing.T) {
	// Both instances are constructed before the real MeterProvider is
	// installed below, mirroring how main.go actually runs: application.New
	// happens before the telemetry hook's Up installs the SDK. Since the
	// OpenTelemetry global meter binds its delegated instruments to the
	// first real MeterProvider it ever sees (for the lifetime of the
	// process, see go.opentelemetry.io/otel/internal/global's
	// delegateMeterOnce), any metric recorded synchronously during New,
	// before that installation, would be silently dropped by the
	// still-noop delegate. Constructing here, before SetMeterProvider,
	// keeps this test honest about that ordering.
	failingInstance := application.New()
	succeedingInstance := application.New(application.WithBuildInfo("1.2.3", "abc123", "development"))

	reader := sdkmetric.NewManualReader()
	provider := sdkmetric.NewMeterProvider(sdkmetric.WithReader(reader))
	previous := otel.GetMeterProvider()
	otel.SetMeterProvider(provider)
	defer otel.SetMeterProvider(previous)

	// A failing instance exercises the hook duration and failure metrics.
	failing := errors.New("boom")
	failingInstance.Append(application.Hook{
		Name: "failing-hook",
		Up:   func(context.Context) error { return failing },
	})
	if err := failingInstance.Up(context.Background()); err == nil {
		t.Fatal("Up returned nil error, want the hook failure")
	}

	// The succeeding instance, built with build info, exercises the ready
	// and info gauges across a full Up/Down cycle.
	succeedingInstance.Append(application.Hook{
		Name: "ok-hook",
		Up:   func(context.Context) error { return nil },
	})
	if err := succeedingInstance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := succeedingInstance.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}

	names := collectMetricNames(t, reader)
	for _, wantMetric := range []string{
		"frappe.application.hook.duration",
		"frappe.application.hook.failures",
		"frappe.application.ready",
		"frappe.application.info",
	} {
		if !names[wantMetric] {
			t.Errorf("expected %s to be recorded, got %v", wantMetric, names)
		}
	}
}
