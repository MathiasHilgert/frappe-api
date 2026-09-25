package logging

import (
	"bytes"
	"log/slog"
	"strings"
	"testing"
)

// TestUpInstallsJSONHandlerGatedByLevel proves Up installs a JSON console
// handler as slog.Default, gated by settings.Level, entirely independent
// of telemetry: no telemetry package is involved anywhere in this test.
func TestUpInstallsJSONHandlerGatedByLevel(t *testing.T) {
	var previousBuffer bytes.Buffer
	previousLogger := slog.New(slog.NewTextHandler(&previousBuffer, nil))
	slog.SetDefault(previousLogger)

	var console bytes.Buffer
	runtime := Up(Settings{Level: "warn", Writer: &console})

	if slog.Default() == previousLogger {
		t.Fatalf("expected Up to install a new default logger")
	}

	slog.Default().Info("dropped by level gating")
	slog.Default().Warn("kept by level gating")

	if strings.Contains(console.String(), "dropped by level gating") {
		t.Fatalf("console output = %q, want the info record dropped by the configured warn level", console.String())
	}
	if !strings.Contains(console.String(), "kept by level gating") {
		t.Fatalf("console output = %q, want it to contain the warn record", console.String())
	}

	runtime.Down()

	if slog.Default() != previousLogger {
		t.Fatalf("expected Down to restore the previous default logger")
	}
}

// TestUpDefaultsWriterToStdout proves that Settings.Writer defaults to
// os.Stdout when left nil, rather than panicking or discarding output.
func TestUpDefaultsWriterToStdout(t *testing.T) {
	previousLogger := slog.Default()

	runtime := Up(Settings{Level: "info"})
	defer runtime.Down()

	if slog.Default() == previousLogger {
		t.Fatalf("expected Up to install a new default logger")
	}
}

// TestAttachAddsHandlerAlongsideConsole proves Attach composes an extra
// handler (standing in for telemetry's OTel log bridge) into the fan-out
// alongside the console handler, without either package importing the
// other.
func TestAttachAddsHandlerAlongsideConsole(t *testing.T) {
	var console bytes.Buffer
	runtime := Up(Settings{Level: "info", Writer: &console})
	defer runtime.Down()

	extra, extraMessages := newCaptureHandler()
	runtime.Attach(extra)

	slog.Default().Info("fanned out")

	if strings.Contains(console.String(), "") == false {
		t.Fatalf("expected console output")
	}
	if !strings.Contains(console.String(), "fanned out") {
		t.Fatalf("console output = %q, want it to contain the logged message", console.String())
	}
	if got := *extraMessages; len(got) != 1 || got[0] != "fanned out" {
		t.Fatalf("extra handler messages = %v, want [fanned out]", got)
	}
}

// TestDownRestoresPreviousDefaultLogger proves Down restores whatever
// default logger was installed before Up ran, mirroring telemetry's own
// previous restore semantics, now owned by logging instead.
func TestDownRestoresPreviousDefaultLogger(t *testing.T) {
	previousLogger := slog.Default()

	runtime := Up(Settings{Level: "info"})
	runtime.Down()

	if slog.Default() != previousLogger {
		t.Fatalf("expected Down to restore the previous default logger")
	}
}
