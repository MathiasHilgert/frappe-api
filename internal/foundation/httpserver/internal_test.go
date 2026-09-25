package httpserver

import (
	"bytes"
	"context"
	"log/slog"
	"net/http"
	"net/http/httptest"
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

// TestStatusRecorderSupportsResponseControllerFlush verifies that
// statusRecorder implements Unwrap() http.ResponseWriter, so
// http.NewResponseController can reach the underlying ResponseWriter's
// optional Flusher (and, in production, Hijacker or SetWriteDeadline)
// support through the wrapper instead of losing it.
func TestStatusRecorderSupportsResponseControllerFlush(t *testing.T) {
	httpRecorder := httptest.NewRecorder()
	recorder := &statusRecorder{ResponseWriter: httpRecorder}

	controller := http.NewResponseController(recorder)
	if err := controller.Flush(); err != nil {
		t.Fatalf("Flush returned unexpected error: %v", err)
	}
	if !httpRecorder.Flushed {
		t.Fatal("expected the underlying ResponseWriter to be flushed")
	}
}

// TestAccessLogDefaultsToStatus200WhenHandlerWritesNothing verifies that a
// handler which calls neither WriteHeader nor Write is reported as status
// 200 in the access log, matching net/http's own default, instead of the
// zero value statusRecorder.status would otherwise be left at.
func TestAccessLogDefaultsToStatus200WhenHandlerWritesNothing(t *testing.T) {
	var logBuffer bytes.Buffer
	logger := slog.New(slog.NewTextHandler(&logBuffer, nil))

	apiMux := http.NewServeMux()
	apiMux.HandleFunc("GET /noop", func(http.ResponseWriter, *http.Request) {})

	handler := accessLogMiddleware(logger, apiMux)(apiMux)

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/noop", nil)
	handler.ServeHTTP(recorder, request)

	logged := logBuffer.String()
	if !strings.Contains(logged, "status=200") {
		t.Fatalf("access log missing default status 200: %s", logged)
	}
}

// TestRequestIDAllowlist table-tests isValidRequestID against the
// documented allowlist ^[A-Za-z0-9._-]{1,128}$, instead of only rejecting
// control characters and length.
func TestRequestIDAllowlist(t *testing.T) {
	tests := []struct {
		name string
		id   string
		want bool
	}{
		{name: "empty", id: "", want: false},
		{name: "simple alphanumeric", id: "abc123", want: true},
		{name: "uuid", id: "29730131-a3bc-45e0-886b-073e927861e5", want: true},
		{name: "dots and underscores", id: "a.b_c-d", want: true},
		{name: "contains space", id: "bad id", want: false},
		{name: "contains slash", id: "bad/id", want: false},
		{name: "contains control character", id: "bad\nid", want: false},
		{name: "contains unicode", id: "bad-idé", want: false},
		{name: "exactly max length", id: strings.Repeat("a", maxRequestIDLength), want: true},
		{name: "over max length", id: strings.Repeat("a", maxRequestIDLength+1), want: false},
	}

	for _, testCase := range tests {
		t.Run(testCase.name, func(t *testing.T) {
			if got := isValidRequestID(testCase.id); got != testCase.want {
				t.Fatalf("isValidRequestID(%q) = %v, want %v", testCase.id, got, testCase.want)
			}
		})
	}
}

// TestDocumentationDisabledAlsoClearsSchemasPath verifies that disabling
// documentation also disables /schemas/..., not just /docs and
// /openapi.json, since it exposes the same internal API shape.
func TestDocumentationDisabledAlsoClearsSchemasPath(t *testing.T) {
	server := New(internalTestSettings())

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/schemas/PingOutput", nil)
	server.Handler().ServeHTTP(recorder, request)
	if recorder.Code == http.StatusNotFound {
		t.Fatalf("/schemas/... returned 404 while documentation is enabled")
	}

	settings := internalTestSettings()
	settings.DocumentationEnabled = false
	disabledServer := New(settings)

	recorder = httptest.NewRecorder()
	request = httptest.NewRequest(http.MethodGet, "/schemas/PingOutput", nil)
	disabledServer.Handler().ServeHTTP(recorder, request)
	if recorder.Code != http.StatusNotFound {
		t.Fatalf("/schemas/... status = %d, want %d while documentation is disabled", recorder.Code, http.StatusNotFound)
	}
}
