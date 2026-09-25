package logging

import (
	"bytes"
	"context"
	"log/slog"
	"strings"
	"testing"
)

// captureHandler is a minimal slog.Handler that appends every message it
// receives, for asserting which handlers a fanoutHandler forwarded a
// record to.
type captureHandler struct {
	messages *[]string
}

func newCaptureHandler() (*captureHandler, *[]string) {
	messages := []string{}
	return &captureHandler{messages: &messages}, &messages
}

func (handler *captureHandler) Enabled(context.Context, slog.Level) bool { return true }

func (handler *captureHandler) Handle(_ context.Context, record slog.Record) error {
	*handler.messages = append(*handler.messages, record.Message)
	return nil
}

func (handler *captureHandler) WithAttrs([]slog.Attr) slog.Handler { return handler }
func (handler *captureHandler) WithGroup(string) slog.Handler      { return handler }

// TestFanoutHandlerForwardsToEveryHandler proves a record reaches every
// configured handler, not just the first one.
func TestFanoutHandlerForwardsToEveryHandler(t *testing.T) {
	first, firstMessages := newCaptureHandler()
	second, secondMessages := newCaptureHandler()

	logger := slog.New(newFanoutHandler(slog.LevelInfo, first, second))
	logger.Info("hello")

	if got := *firstMessages; len(got) != 1 || got[0] != "hello" {
		t.Fatalf("first handler messages = %v, want [hello]", got)
	}
	if got := *secondMessages; len(got) != 1 || got[0] != "hello" {
		t.Fatalf("second handler messages = %v, want [hello]", got)
	}
}

// TestFanoutHandlerGatesByConfiguredLevel proves a record below the
// configured minimum level never reaches any forwarded handler, while one
// at or above it does.
func TestFanoutHandlerGatesByConfiguredLevel(t *testing.T) {
	handler, messages := newCaptureHandler()

	logger := slog.New(newFanoutHandler(slog.LevelWarn, handler))
	logger.Debug("dropped")
	logger.Info("also dropped")
	logger.Warn("kept")

	if got := *messages; len(got) != 1 || got[0] != "kept" {
		t.Fatalf("messages = %v, want [kept]", got)
	}
}

// TestFanoutHandlerConsoleReceivesRecordsAlongsideOTel proves a
// JSON console handler writing to a buffer still receives log records
// when fanned out alongside another handler, so switching the default
// logger to export through OTel does not silence stdout logging.
func TestFanoutHandlerConsoleReceivesRecordsAlongsideOTel(t *testing.T) {
	var console bytes.Buffer
	consoleHandler := slog.NewJSONHandler(&console, &slog.HandlerOptions{Level: slog.LevelDebug})
	other, _ := newCaptureHandler()

	logger := slog.New(newFanoutHandler(slog.LevelInfo, consoleHandler, other))
	logger.Info("service starting")

	if !strings.Contains(console.String(), "service starting") {
		t.Fatalf("console output = %q, want it to contain the logged message", console.String())
	}
}

func TestParseLoggingLevel(t *testing.T) {
	cases := []struct {
		input string
		want  slog.Level
	}{
		{"debug", slog.LevelDebug},
		{"info", slog.LevelInfo},
		{"warn", slog.LevelWarn},
		{"error", slog.LevelError},
		{"", slog.LevelInfo},
		{"unknown", slog.LevelInfo},
	}

	for _, testCase := range cases {
		if got := parseLoggingLevel(testCase.input); got != testCase.want {
			t.Errorf("parseLoggingLevel(%q) = %v, want %v", testCase.input, got, testCase.want)
		}
	}
}
