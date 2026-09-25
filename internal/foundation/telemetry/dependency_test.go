package telemetry

import (
	"context"
	"errors"
	"log/slog"
	"testing"

	"go.opentelemetry.io/otel"
	otellogglobal "go.opentelemetry.io/otel/log/global"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
)

// TestUpNeverTouchesDefaultLoggerAndExposesLogHandler proves telemetry no
// longer replaces slog.Default itself: logging owns that. Up instead
// exposes the OTel log bridge handler through SDK.LogHandler, ready for
// the composition root to attach to the process logger once telemetry
// succeeds.
func TestUpNeverTouchesDefaultLoggerAndExposesLogHandler(t *testing.T) {
	previousLogger := slog.Default()

	settings := Settings{
		Enabled:               true,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if slog.Default() != previousLogger {
		t.Fatalf("expected Up to leave slog.Default untouched")
	}

	if value.LogHandler() == nil {
		t.Fatalf("expected LogHandler to return a non-nil handler when telemetry is enabled")
	}

	// Down's error here is expected and unrelated to this test: there is no
	// real OTLP collector listening at the default endpoint, so flushing
	// the trace and metric providers fails.
	_ = Down(context.Background(), value)

	if slog.Default() != previousLogger {
		t.Fatalf("expected Down to leave slog.Default untouched")
	}
}

// TestLogHandlerIsNilWhenDisabled proves SDK.LogHandler returns nil when
// telemetry is disabled, so the composition root never attaches a nil
// handler on top of the process logger.
func TestLogHandlerIsNilWhenDisabled(t *testing.T) {
	settings := Settings{
		Enabled:               false,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if value.LogHandler() != nil {
		t.Fatalf("expected LogHandler to return nil when telemetry is disabled")
	}
}

func TestUpDownWhenDisabledKeepsNoopGlobalProviders(t *testing.T) {
	settings := Settings{
		Enabled:               false,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if otel.GetTracerProvider() == nil {
		t.Fatalf("expected a non-nil global tracer provider")
	}

	if err := Down(context.Background(), value); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}

func TestDependencyNameIsSet(t *testing.T) {
	if DependencyName == "" {
		t.Fatalf("expected a non-empty dependency name")
	}
}

// TestUpSetsGlobalLogLoggerProviderAndDownRestoresPrevious proves that Up
// also installs the built LoggerProvider through
// go.opentelemetry.io/otel/log/global.SetLoggerProvider, so code using the
// OTel Logs API (log.Logger) directly, not just slog, is bridged too. Down
// restores whatever global log provider was in place before Up ran.
func TestUpSetsGlobalLogLoggerProviderAndDownRestoresPrevious(t *testing.T) {
	previousProvider := otellogglobal.GetLoggerProvider()

	settings := Settings{
		Enabled:               true,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if otellogglobal.GetLoggerProvider() == previousProvider {
		t.Fatalf("expected Up to install a new global log.LoggerProvider")
	}

	_ = Down(context.Background(), value)

	if otellogglobal.GetLoggerProvider() != previousProvider {
		t.Fatalf("expected Down to restore the previous global log.LoggerProvider")
	}
}

// TestUpCleansUpAndLeavesGlobalsUntouchedOnLateFailure proves that when a
// step after the providers are built fails, Up shuts down every provider
// it already built and never installs any of them as global providers, so
// no orphaned provider is left reachable through the OTel globals.
func TestUpCleansUpAndLeavesGlobalsUntouchedOnLateFailure(t *testing.T) {
	previousTracerProvider := otel.GetTracerProvider()
	previousMeterProvider := otel.GetMeterProvider()
	previousLogProvider := otellogglobal.GetLoggerProvider()

	injected := errors.New("injected instrumentation failure")
	restore := setStartInstrumentationForTest(func(*sdkmetric.MeterProvider) error {
		return injected
	})
	defer restore()

	settings := Settings{
		Enabled:               true,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err == nil {
		t.Fatal("Up returned nil error, want the injected instrumentation failure")
	}
	if !errors.Is(err, injected) {
		t.Fatalf("Up error = %v, want it to wrap %v", err, injected)
	}

	if value != (SDK{}) {
		t.Fatalf("Up returned a non-zero SDK on failure: %+v", value)
	}

	if otel.GetTracerProvider() != previousTracerProvider {
		t.Fatal("expected the global tracer provider to be left untouched on failure")
	}
	if otel.GetMeterProvider() != previousMeterProvider {
		t.Fatal("expected the global meter provider to be left untouched on failure")
	}
	if otellogglobal.GetLoggerProvider() != previousLogProvider {
		t.Fatal("expected the global log provider to be left untouched on failure")
	}
}
