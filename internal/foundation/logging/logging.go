// Package logging builds and installs the process-wide slog logger,
// independent of telemetry. Logging must not depend on telemetry: it is
// installed first, before any other dependency, so every log line the
// process produces, including lifecycle logs, is JSON and level-gated
// regardless of whether telemetry export is enabled.
package logging

import (
	"io"
	"log/slog"
	"os"
	"sync"
)

// Settings is the subset of application-wide configuration the process
// logger needs: its minimum level and where its JSON console handler
// writes.
type Settings struct {
	// Writer is where the JSON console handler writes. Defaults to
	// os.Stdout when nil.
	Writer io.Writer
	// Level mirrors configuration.Logging.Level's validated values
	// ("debug", "info", "warn", "error"). An empty or unrecognized value
	// defaults to "info".
	Level string
}

// Runtime is the installed process logger. It owns the fan-out handler
// slog.Default is pointed at, so Attach can compose further handlers
// (such as telemetry's OTel log bridge) in later, once they become
// available, and Down can restore whatever default logger was installed
// before Up ran.
type Runtime struct {
	console  slog.Handler
	extra    slog.Handler
	previous *slog.Logger
	level    slog.Level
	mu       sync.Mutex
}

// Up builds the process logger from settings - a JSON handler over
// settings.Writer (os.Stdout by default), gated by settings.Level -
// installs it as slog.Default, and returns the Runtime that owns it. Up
// is meant to run first, independent of any other dependency (in
// particular, independent of whether telemetry is enabled), so every log
// line the process produces from that point on is JSON and level-gated.
func Up(settings Settings) *Runtime {
	writer := settings.Writer
	if writer == nil {
		writer = os.Stdout
	}

	runtime := &Runtime{
		level:    parseLoggingLevel(settings.Level),
		console:  slog.NewJSONHandler(writer, &slog.HandlerOptions{Level: slog.LevelDebug}),
		previous: slog.Default(),
	}

	slog.SetDefault(slog.New(runtime.build()))

	return runtime
}

// Attach adds handler to the fan-out this Runtime installed as
// slog.Default, alongside the console handler, and rebuilds slog.Default
// so every subsequent log record reaches both. This is how the
// composition root wires the telemetry OTel log bridge in, once
// telemetry.Up has succeeded, without logging importing or otherwise
// depending on telemetry. Calling Attach again replaces the previously
// attached handler.
func (runtime *Runtime) Attach(handler slog.Handler) {
	runtime.mu.Lock()
	defer runtime.mu.Unlock()

	runtime.extra = handler
	slog.SetDefault(slog.New(runtime.build()))
}

// Down restores whatever default logger was installed before Up ran.
func (runtime *Runtime) Down() {
	runtime.mu.Lock()
	defer runtime.mu.Unlock()

	if runtime.previous != nil {
		slog.SetDefault(runtime.previous)
	}
}

// build returns the current fan-out handler: the console handler, plus
// any handler Attach added, gated by the configured level. Callers must
// hold runtime.mu.
func (runtime *Runtime) build() slog.Handler {
	handlers := []slog.Handler{runtime.console}
	if runtime.extra != nil {
		handlers = append(handlers, runtime.extra)
	}
	return newFanoutHandler(runtime.level, handlers...)
}
