package telemetry

import (
	"bytes"
	"context"
	"errors"
	"io"
	"log/slog"
	"os"
	"strings"
	"testing"

	"go.opentelemetry.io/otel"
	otellogglobal "go.opentelemetry.io/otel/log/global"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
)

// captureStdout redirects os.Stdout for the duration of run, and returns
// everything written to it. It restores the original os.Stdout before
// returning, even if run panics.
func captureStdout(t *testing.T, run func()) string {
	t.Helper()

	reader, writer, err := os.Pipe()
	if err != nil {
		t.Fatalf("create pipe: %v", err)
	}

	previous := os.Stdout
	os.Stdout = writer
	defer func() { os.Stdout = previous }()

	done := make(chan string, 1)
	go func() {
		var buffer bytes.Buffer
		_, _ = io.Copy(&buffer, reader)
		done <- buffer.String()
	}()

	run()

	_ = writer.Close()
	return <-done
}

// TestUpInstallsFanoutLoggerGatedByLevelAndDownRestoresPrevious proves
// that Up no longer replaces the default logger outright: it installs a
// fan-out handler that still writes to the console (stdout), gated by the
// configured Logging level, and Down restores whatever default logger was
// in place before Up ran.
func TestUpInstallsFanoutLoggerGatedByLevelAndDownRestoresPrevious(t *testing.T) {
	var previousBuffer bytes.Buffer
	previousLogger := slog.New(slog.NewTextHandler(&previousBuffer, nil))
	slog.SetDefault(previousLogger)

	settings := Settings{
		Enabled:               true,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
		LoggingLevel:          "warn",
	}

	var value SDK
	var upErr error

	output := captureStdout(t, func() {
		value, upErr = Up(context.Background(), settings)
		if upErr != nil {
			return
		}
		slog.Default().Info("dropped by level gating")
		slog.Default().Warn("kept by level gating")
	})

	if upErr != nil {
		t.Fatalf("Up returned unexpected error: %v", upErr)
	}

	if slog.Default() == previousLogger {
		t.Fatalf("expected Up to install a new default logger")
	}

	if strings.Contains(output, "dropped by level gating") {
		t.Fatalf("console output = %q, want the info record dropped by the configured warn level", output)
	}
	if !strings.Contains(output, "kept by level gating") {
		t.Fatalf("console output = %q, want it to contain the warn record", output)
	}

	// Down's error here is expected and unrelated to this test: there is no
	// real OTLP collector listening at the default endpoint, so flushing
	// the trace and metric providers fails. Restoring the previous default
	// logger does not depend on that flush succeeding.
	_ = Down(context.Background(), value)

	if slog.Default() != previousLogger {
		t.Fatalf("expected Down to restore the previous default logger")
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
		LoggingLevel:          "info",
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
// no orphaned provider is left reachable through the OTel globals or
// slog.Default.
func TestUpCleansUpAndLeavesGlobalsUntouchedOnLateFailure(t *testing.T) {
	previousTracerProvider := otel.GetTracerProvider()
	previousMeterProvider := otel.GetMeterProvider()
	previousLogProvider := otellogglobal.GetLoggerProvider()
	previousLogger := slog.Default()

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
		LoggingLevel:          "info",
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
	if slog.Default() != previousLogger {
		t.Fatal("expected the default slog logger to be left untouched on failure")
	}
}
