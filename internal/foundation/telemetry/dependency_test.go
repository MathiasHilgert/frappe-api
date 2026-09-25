package telemetry

import (
	"bytes"
	"context"
	"io"
	"log/slog"
	"os"
	"strings"
	"testing"

	"go.opentelemetry.io/otel"
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
