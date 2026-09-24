package httpserver_test

import (
	"bytes"
	"context"
	"encoding/json"
	"log/slog"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

// testSettings returns Settings with short timeouts and an always-ready
// ready function, suitable for exercising the server in tests.
func testSettings(logBuffer *bytes.Buffer, ready func() bool, documentationEnabled bool) httpserver.Settings {
	var logger *slog.Logger
	if logBuffer != nil {
		logger = slog.New(slog.NewTextHandler(logBuffer, nil))
	} else {
		logger = slog.New(slog.NewTextHandler(bytes.NewBuffer(nil), nil))
	}

	return httpserver.Settings{
		Title:                "frappe-api",
		Version:              "0.0.0",
		ReadHeaderTimeout:    time.Second,
		ReadTimeout:          time.Second,
		WriteTimeout:         time.Second,
		IdleTimeout:          time.Second,
		MaxHeaderBytes:       1 << 20,
		MaxBodyBytes:         1 << 20,
		DocumentationEnabled: documentationEnabled,
		Ready:                ready,
		Logger:               logger,
	}
}

// TestHealthLiveAlwaysReportsOK verifies that /health/live returns 200 as
// soon as the server handles requests, regardless of readiness.
func TestHealthLiveAlwaysReportsOK(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return false }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/health/live", nil)
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusOK)
	}
}

// TestHealthReadyReports200WhenReady verifies that /health/ready returns
// 200 when the injected ready function reports true.
func TestHealthReadyReports200WhenReady(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/health/ready", nil)
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusOK)
	}
}

// TestHealthReadyReports503WhenNotReady verifies that /health/ready
// returns 503 when the injected ready function reports false.
func TestHealthReadyReports503WhenNotReady(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return false }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/health/ready", nil)
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusServiceUnavailable)
	}
}

// TestRequestIDIsGeneratedWhenAbsent verifies that a request without an
// inbound X-Request-ID header receives a generated one, echoed in the
// response.
func TestRequestIDIsGeneratedWhenAbsent(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/", nil)
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Header().Get("X-Request-ID") == "" {
		t.Fatal("response is missing a generated X-Request-ID header")
	}
}

// TestRequestIDIsPropagatedWhenValid verifies that a valid inbound
// X-Request-ID header is echoed back unchanged instead of being replaced.
func TestRequestIDIsPropagatedWhenValid(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/", nil)
	request.Header.Set("X-Request-ID", "caller-supplied-id")
	server.Handler().ServeHTTP(recorder, request)

	if got := recorder.Header().Get("X-Request-ID"); got != "caller-supplied-id" {
		t.Fatalf("X-Request-ID = %q, want %q", got, "caller-supplied-id")
	}
}

// TestRequestIDIsReplacedWhenInvalid verifies that an invalid inbound
// X-Request-ID header (containing control characters) is not trusted and
// a fresh one is generated instead.
func TestRequestIDIsReplacedWhenInvalid(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/", nil)
	request.Header.Set("X-Request-ID", "bad\nid")
	server.Handler().ServeHTTP(recorder, request)

	if got := recorder.Header().Get("X-Request-ID"); got == "bad\nid" || got == "" {
		t.Fatalf("X-Request-ID = %q, want a freshly generated id", got)
	}
}

// TestPanicRecoveryReturnsProblemJSON verifies that a panicking handler is
// recovered and turned into an RFC 9457 problem+json 500 response instead
// of crashing the server or leaking internals.
func TestPanicRecoveryReturnsProblemJSON(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	type panicOutput struct{}
	huma.Register(server.V1(), huma.Operation{
		OperationID: "panics",
		Method:      http.MethodGet,
		Path:        "/panics",
	}, func(context.Context, *struct{}) (*panicOutput, error) {
		panic("boom: internal secret detail")
	})

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/panics", nil)
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusInternalServerError {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusInternalServerError)
	}
	if contentType := recorder.Header().Get("Content-Type"); !strings.Contains(contentType, "application/problem+json") {
		t.Fatalf("Content-Type = %q, want application/problem+json", contentType)
	}
	if strings.Contains(recorder.Body.String(), "internal secret detail") {
		t.Fatal("response body leaks the panic's internal detail")
	}

	var body map[string]any
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("response body is not valid JSON: %v", err)
	}
	if status, _ := body["status"].(float64); int(status) != http.StatusInternalServerError {
		t.Fatalf("body status = %v, want %d", body["status"], http.StatusInternalServerError)
	}
}

// TestAccessLogRecordsRequestFields verifies that a completed request is
// logged with method, route pattern, status and request id.
func TestAccessLogRecordsRequestFields(t *testing.T) {
	var logBuffer bytes.Buffer
	server := httpserver.New(testSettings(&logBuffer, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/", nil)
	request.Header.Set("X-Request-ID", "log-test-id")
	server.Handler().ServeHTTP(recorder, request)

	logged := logBuffer.String()
	if !strings.Contains(logged, "GET") {
		t.Fatalf("access log missing method: %s", logged)
	}
	if !strings.Contains(logged, "log-test-id") {
		t.Fatalf("access log missing request id: %s", logged)
	}
}

// TestHealthEndpointsAreNotAccessLogged verifies that health checks do not
// pollute the access log, keeping it focused on real API traffic.
func TestHealthEndpointsAreNotAccessLogged(t *testing.T) {
	var logBuffer bytes.Buffer
	server := httpserver.New(testSettings(&logBuffer, func() bool { return true }, true))

	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/health/live", nil)
	server.Handler().ServeHTTP(recorder, request)

	if logBuffer.Len() != 0 {
		t.Fatalf("access log recorded a health check: %s", logBuffer.String())
	}
}

// TestDocumentationEndpointsAreServedWhenEnabled verifies that /docs and
// /openapi.json respond when DocumentationEnabled is true.
func TestDocumentationEndpointsAreServedWhenEnabled(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))

	for _, path := range []string{"/docs", "/openapi.json"} {
		recorder := httptest.NewRecorder()
		request := httptest.NewRequest(http.MethodGet, path, nil)
		server.Handler().ServeHTTP(recorder, request)

		if recorder.Code == http.StatusNotFound {
			t.Fatalf("%s returned 404 while documentation is enabled", path)
		}
	}
}

// TestDocumentationEndpointsAreDisabledWhenToggledOff verifies that /docs
// and /openapi.json return 404 when DocumentationEnabled is false.
func TestDocumentationEndpointsAreDisabledWhenToggledOff(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, false))

	for _, path := range []string{"/docs", "/openapi.json"} {
		recorder := httptest.NewRecorder()
		request := httptest.NewRequest(http.MethodGet, path, nil)
		server.Handler().ServeHTTP(recorder, request)

		if recorder.Code != http.StatusNotFound {
			t.Fatalf("%s status = %d, want %d while documentation is disabled", path, recorder.Code, http.StatusNotFound)
		}
	}
}

// TestUpFailsWhenPortIsOccupied verifies that Listen fails synchronously
// when the configured port cannot be bound, instead of silently starting
// in the background and failing later.
func TestUpFailsWhenPortIsOccupied(t *testing.T) {
	occupier, err := net.Listen("tcp", ":0")
	if err != nil {
		t.Fatalf("failed to reserve a port for the test: %v", err)
	}
	defer func() { _ = occupier.Close() }()

	port := occupier.Addr().(*net.TCPAddr).Port

	settings := testSettings(nil, func() bool { return true }, true)
	settings.Port = port
	server := httpserver.New(settings)

	if err := server.Listen(); err == nil {
		_ = server.Shutdown(t.Context())
		t.Fatal("Listen returned nil error for an occupied port")
	}
}

// TestListenThenShutdownIsGraceful verifies that a server started with
// Listen serves requests and then shuts down cleanly through Shutdown.
func TestListenThenShutdownIsGraceful(t *testing.T) {
	settings := testSettings(nil, func() bool { return true }, true)
	settings.Port = 0
	server := httpserver.New(settings)

	if err := server.Listen(); err != nil {
		t.Fatalf("Listen returned unexpected error: %v", err)
	}

	address := server.Addr()
	if address == "" {
		t.Fatal("Addr() is empty after Listen")
	}

	response, err := http.Get("http://" + address + "/health/live")
	if err != nil {
		t.Fatalf("GET /health/live failed: %v", err)
	}
	_ = response.Body.Close()
	if response.StatusCode != http.StatusOK {
		t.Fatalf("status = %d, want %d", response.StatusCode, http.StatusOK)
	}

	if err := server.Shutdown(t.Context()); err != nil {
		t.Fatalf("Shutdown returned unexpected error: %v", err)
	}
}
