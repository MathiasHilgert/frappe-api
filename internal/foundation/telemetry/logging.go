package telemetry

import (
	"context"
	"errors"
	"log/slog"
)

// fanoutHandler is a slog.Handler that forwards every record it is given
// to a fixed list of handlers, after gating on a single configured
// minimum level. Centralizing the level check here means every forwarded
// handler receives exactly the same records, regardless of any level
// configuration of its own.
type fanoutHandler struct {
	handlers []slog.Handler
	level    slog.Level
}

var _ slog.Handler = (*fanoutHandler)(nil)

// newFanoutHandler returns a fanoutHandler that forwards records at or
// above level to every one of handlers.
func newFanoutHandler(level slog.Level, handlers ...slog.Handler) *fanoutHandler {
	return &fanoutHandler{level: level, handlers: handlers}
}

// Enabled reports whether level is at or above the configured minimum
// level. This is the single point where the configured logging level is
// enforced: slog.Logger checks Enabled before building a Record and
// calling Handle, so a disabled level never reaches any forwarded
// handler.
func (handler *fanoutHandler) Enabled(_ context.Context, level slog.Level) bool {
	return level >= handler.level
}

// Handle forwards a copy of record to every configured handler, joining
// any errors returned. Each handler receives its own clone because
// slog.Handler.Handle documents that implementations may retain or
// mutate the Record's shared state (such as its attribute slice) between
// calls.
func (handler *fanoutHandler) Handle(ctx context.Context, record slog.Record) error {
	var errs []error
	for _, next := range handler.handlers {
		if err := next.Handle(ctx, record.Clone()); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// WithAttrs returns a fanoutHandler whose forwarded handlers each carry
// attrs, preserving the configured level.
func (handler *fanoutHandler) WithAttrs(attrs []slog.Attr) slog.Handler {
	next := make([]slog.Handler, len(handler.handlers))
	for index, child := range handler.handlers {
		next[index] = child.WithAttrs(attrs)
	}
	return &fanoutHandler{level: handler.level, handlers: next}
}

// WithGroup returns a fanoutHandler whose forwarded handlers each open
// group name, preserving the configured level.
func (handler *fanoutHandler) WithGroup(name string) slog.Handler {
	next := make([]slog.Handler, len(handler.handlers))
	for index, child := range handler.handlers {
		next[index] = child.WithGroup(name)
	}
	return &fanoutHandler{level: handler.level, handlers: next}
}

// parseLoggingLevel maps configuration.Logging.Level's values
// ("debug", "info", "warn", "error") to a slog.Level. This package
// intentionally does not import internal/foundation/configuration (a
// foundation package must not depend on another one), so it mirrors that
// small, validated set of values instead. Any unrecognized or empty
// value defaults to slog.LevelInfo, the same default
// configuration.Logging.Level carries.
func parseLoggingLevel(level string) slog.Level {
	switch level {
	case "debug":
		return slog.LevelDebug
	case "warn":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}
