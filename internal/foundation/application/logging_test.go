package application_test

import (
	"bytes"
	"context"
	"io"
	"log/slog"
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// TestHookLoggingResolvesDefaultLoggerLazily proves that when no explicit
// logger is given through WithLogger, the Application resolves
// slog.Default() at the time each hook phase logs, not once at New. This
// matters because telemetry's Up (registered as the first hook) installs
// its own default logger; capturing slog.Default() at New would freeze
// the Application onto whatever logger was default before telemetry ever
// ran, so lifecycle logs would never reach the installed pipeline.
func TestHookLoggingResolvesDefaultLoggerLazily(t *testing.T) {
	var before bytes.Buffer
	slog.SetDefault(slog.New(slog.NewTextHandler(&before, nil)))

	instance := application.New()

	var after bytes.Buffer
	slog.SetDefault(slog.New(slog.NewTextHandler(&after, nil)))
	t.Cleanup(func() { slog.SetDefault(slog.New(slog.NewTextHandler(io.Discard, nil))) })

	instance.Append(application.Hook{
		Name: "hook",
		Up:   func(context.Context) error { return nil },
	})
	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if before.Len() != 0 {
		t.Fatalf("expected nothing logged to the pre-New default logger, got %q", before.String())
	}
	if !strings.Contains(after.String(), "hook") {
		t.Fatalf("expected the post-New default logger to receive the hook log, got %q", after.String())
	}
}

// TestHookLoggingHonorsExplicitLogger proves that WithLogger still wins
// over the default logger, at any point in time.
func TestHookLoggingHonorsExplicitLogger(t *testing.T) {
	var explicit bytes.Buffer
	instance := application.New(application.WithLogger(slog.New(slog.NewTextHandler(&explicit, nil))))

	var laterDefault bytes.Buffer
	slog.SetDefault(slog.New(slog.NewTextHandler(&laterDefault, nil)))
	t.Cleanup(func() { slog.SetDefault(slog.New(slog.NewTextHandler(io.Discard, nil))) })

	instance.Append(application.Hook{
		Name: "hook",
		Up:   func(context.Context) error { return nil },
	})
	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if !strings.Contains(explicit.String(), "hook") {
		t.Fatalf("expected the explicit logger to receive the hook log, got %q", explicit.String())
	}
	if laterDefault.Len() != 0 {
		t.Fatalf("expected nothing logged to the default logger when an explicit one was given, got %q", laterDefault.String())
	}
}
