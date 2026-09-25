package httpserver

import (
	"bytes"
	"context"
	"log/slog"
	"strings"
	"testing"
	"time"
)

// internalTestSettings returns Settings sufficient to exercise Server.New
// directly from this package's own white-box tests, mirroring
// httpserver_test.go's external testSettings without depending on it
// across packages.
func internalTestSettings() Settings {
	return Settings{
		Title:                "frappe-api",
		Version:              "0.0.0",
		ReadHeaderTimeout:    time.Second,
		ReadTimeout:          time.Second,
		WriteTimeout:         time.Second,
		IdleTimeout:          time.Second,
		MaxHeaderBytes:       1 << 20,
		MaxBodyBytes:         1 << 20,
		DocumentationEnabled: true,
		Logger:               slog.New(slog.NewTextHandler(new(bytes.Buffer), nil)),
	}
}

// TestListenReportsFatalServeErrorWhenListenerClosedExternally verifies
// that closing the listener out from under a running Server (simulating
// the listener breaking unexpectedly, as opposed to a graceful Shutdown)
// is logged and reported on Errors(), instead of being silently dropped.
func TestListenReportsFatalServeErrorWhenListenerClosedExternally(t *testing.T) {
	var logBuffer bytes.Buffer
	settings := internalTestSettings()
	settings.Logger = slog.New(slog.NewTextHandler(&logBuffer, nil))
	settings.Port = 0
	server := New(settings)

	if err := server.Listen(); err != nil {
		t.Fatalf("Listen returned unexpected error: %v", err)
	}

	// Close the listener directly, out from under the server: this is not
	// a graceful Shutdown, so Serve must return a non-ErrServerClosed
	// error.
	if err := server.listener.Close(); err != nil {
		t.Fatalf("failed to close listener: %v", err)
	}

	select {
	case err := <-server.Errors():
		if err == nil {
			t.Fatal("Errors() delivered a nil error")
		}
	case <-time.After(2 * time.Second):
		t.Fatal("timed out waiting for Errors() to report the broken listener")
	}

	deadline := time.Now().Add(2 * time.Second)
	for !strings.Contains(logBuffer.String(), "http server stopped unexpectedly") {
		if time.Now().After(deadline) {
			t.Fatalf("expected a fatal Serve error to be logged, got: %s", logBuffer.String())
		}
		time.Sleep(10 * time.Millisecond)
	}
}

// TestListenDoesNotReportGracefulShutdownAsAFatalError verifies that a
// normal Shutdown, which makes Serve return http.ErrServerClosed, is not
// mistaken for a fatal error and does not appear on Errors().
func TestListenDoesNotReportGracefulShutdownAsAFatalError(t *testing.T) {
	settings := internalTestSettings()
	settings.Port = 0
	server := New(settings)

	if err := server.Listen(); err != nil {
		t.Fatalf("Listen returned unexpected error: %v", err)
	}
	if err := server.Shutdown(context.Background()); err != nil {
		t.Fatalf("Shutdown returned unexpected error: %v", err)
	}

	select {
	case err := <-server.Errors():
		t.Fatalf("Errors() unexpectedly reported an error after a graceful Shutdown: %v", err)
	case <-time.After(100 * time.Millisecond):
	}
}
