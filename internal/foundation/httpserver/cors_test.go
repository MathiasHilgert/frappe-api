package httpserver_test

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

const allowedTestOrigin = "https://app.example.com"

// corsServer builds a Server with the given CORS settings and one GET
// /v1/things operation, so tests exercise the real middleware chain.
func corsServer(t *testing.T, cors httpserver.CORSSettings) *httpserver.Server {
	t.Helper()
	settings := testSettings(nil, func() bool { return true }, false)
	settings.CORS = cors
	server := httpserver.New(settings)

	type thingsOutput struct {
		Body struct {
			Name string `json:"name"`
		}
	}
	huma.Get(server.V1(), "/things", func(context.Context, *struct{}) (*thingsOutput, error) {
		output := &thingsOutput{}
		output.Body.Name = "thing"
		return output, nil
	})
	return server
}

func enabledCORS() httpserver.CORSSettings {
	return httpserver.CORSSettings{
		AllowedOrigins: []string{allowedTestOrigin},
		AllowedMethods: []string{http.MethodGet, http.MethodPost},
		AllowedHeaders: []string{"Authorization", "Content-Type", "X-Request-ID"},
		ExposedHeaders: []string{"X-Request-ID", "RateLimit-Limit"},
		MaxAge:         10 * time.Minute,
	}
}

func serve(server *httpserver.Server, request *http.Request) *httptest.ResponseRecorder {
	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)
	return recorder
}

func TestCORSAllowedOriginReceivesAllowOriginAndExposedHeaders(t *testing.T) {
	server := corsServer(t, enabledCORS())
	request := httptest.NewRequest(http.MethodGet, "/v1/things", nil)
	request.Header.Set("Origin", allowedTestOrigin)

	recorder := serve(server, request)

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", recorder.Code)
	}
	if got := recorder.Header().Get("Access-Control-Allow-Origin"); got != allowedTestOrigin {
		t.Fatalf("Access-Control-Allow-Origin = %q, want %q", got, allowedTestOrigin)
	}
	exposed := recorder.Header().Get("Access-Control-Expose-Headers")
	if !strings.Contains(exposed, "X-Request-Id") && !strings.Contains(exposed, "X-Request-ID") {
		t.Fatalf("Access-Control-Expose-Headers = %q, want it to contain X-Request-ID", exposed)
	}
	if got := recorder.Header().Get("Access-Control-Allow-Credentials"); got != "" {
		t.Fatalf("Access-Control-Allow-Credentials = %q, want empty when credentials are disabled", got)
	}
}

func TestCORSDisallowedOriginReceivesNoAllowOrigin(t *testing.T) {
	server := corsServer(t, enabledCORS())
	request := httptest.NewRequest(http.MethodGet, "/v1/things", nil)
	request.Header.Set("Origin", "https://evil.example.com")

	recorder := serve(server, request)

	if got := recorder.Header().Get("Access-Control-Allow-Origin"); got != "" {
		t.Fatalf("Access-Control-Allow-Origin = %q, want empty for a disallowed origin", got)
	}
}

func TestCORSAlwaysSetsVaryOrigin(t *testing.T) {
	server := corsServer(t, enabledCORS())
	for _, origin := range []string{allowedTestOrigin, "https://evil.example.com", ""} {
		request := httptest.NewRequest(http.MethodGet, "/v1/things", nil)
		if origin != "" {
			request.Header.Set("Origin", origin)
		}

		recorder := serve(server, request)

		if !strings.Contains(strings.Join(recorder.Header().Values("Vary"), ","), "Origin") {
			t.Fatalf("origin %q: Vary = %q, want it to contain Origin", origin, recorder.Header().Values("Vary"))
		}
	}
}

func TestCORSPreflightIsAnsweredWith204BeforeRouting(t *testing.T) {
	server := corsServer(t, enabledCORS())
	request := httptest.NewRequest(http.MethodOptions, "/v1/things", nil)
	request.Header.Set("Origin", allowedTestOrigin)
	request.Header.Set("Access-Control-Request-Method", http.MethodPost)
	request.Header.Set("Access-Control-Request-Headers", "content-type")

	recorder := serve(server, request)

	if recorder.Code != http.StatusNoContent {
		t.Fatalf("status = %d, want 204", recorder.Code)
	}
	if got := recorder.Header().Get("Access-Control-Allow-Origin"); got != allowedTestOrigin {
		t.Fatalf("Access-Control-Allow-Origin = %q, want %q", got, allowedTestOrigin)
	}
	if got := recorder.Header().Get("Access-Control-Allow-Methods"); !strings.Contains(got, http.MethodPost) {
		t.Fatalf("Access-Control-Allow-Methods = %q, want it to contain POST", got)
	}
	if got := recorder.Header().Get("Access-Control-Max-Age"); got != "600" {
		t.Fatalf("Access-Control-Max-Age = %q, want 600", got)
	}
	if got := recorder.Header().Get(httpserver.RequestIDHeader); got == "" {
		t.Fatal("preflight response is missing X-Request-ID, so CORS runs outside request id")
	}
}

func TestCORSPreflightForUnknownRouteIsNot404(t *testing.T) {
	server := corsServer(t, enabledCORS())
	request := httptest.NewRequest(http.MethodOptions, "/v1/unknown", nil)
	request.Header.Set("Origin", allowedTestOrigin)
	request.Header.Set("Access-Control-Request-Method", http.MethodGet)

	recorder := serve(server, request)

	if recorder.Code != http.StatusNoContent {
		t.Fatalf("status = %d, want 204", recorder.Code)
	}
}

func TestCORSCredentialsAreAllowedWhenConfigured(t *testing.T) {
	cors := enabledCORS()
	cors.AllowCredentials = true
	server := corsServer(t, cors)
	request := httptest.NewRequest(http.MethodGet, "/v1/things", nil)
	request.Header.Set("Origin", allowedTestOrigin)

	recorder := serve(server, request)

	if got := recorder.Header().Get("Access-Control-Allow-Credentials"); got != "true" {
		t.Fatalf("Access-Control-Allow-Credentials = %q, want true", got)
	}
}

func TestCORSIsDisabledWhenNoOriginIsConfigured(t *testing.T) {
	server := corsServer(t, httpserver.CORSSettings{})
	request := httptest.NewRequest(http.MethodGet, "/v1/things", nil)
	request.Header.Set("Origin", allowedTestOrigin)

	recorder := serve(server, request)

	for _, header := range []string{"Access-Control-Allow-Origin", "Access-Control-Expose-Headers", "Vary"} {
		if got := recorder.Header().Get(header); got != "" {
			t.Fatalf("%s = %q, want no CORS headers when disabled", header, got)
		}
	}
}
