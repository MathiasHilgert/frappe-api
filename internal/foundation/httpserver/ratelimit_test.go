package httpserver_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"net/netip"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

// fakeLimiter records every key it is asked about and answers with a
// fixed decision or error.
type fakeLimiter struct {
	err      error
	keys     []string
	decision httpserver.RateLimitDecision
	mutex    sync.Mutex
}

func (limiter *fakeLimiter) Allow(_ context.Context, key string) (httpserver.RateLimitDecision, error) {
	limiter.mutex.Lock()
	defer limiter.mutex.Unlock()
	limiter.keys = append(limiter.keys, key)
	return limiter.decision, limiter.err
}

func (limiter *fakeLimiter) calledKeys() []string {
	limiter.mutex.Lock()
	defer limiter.mutex.Unlock()
	return append([]string(nil), limiter.keys...)
}

func rateLimitedServer(t *testing.T, logBuffer *bytes.Buffer, rateLimit httpserver.RateLimitSettings) *httpserver.Server {
	t.Helper()
	settings := testSettings(logBuffer, func() bool { return true }, true)
	settings.RateLimit = rateLimit
	server := httpserver.New(settings)

	type pingOutput struct {
		Body struct {
			Message string `json:"message"`
		}
	}
	huma.Get(server.V1(), "/ping", func(context.Context, *struct{}) (*pingOutput, error) {
		output := &pingOutput{}
		output.Body.Message = "pong"
		return output, nil
	})
	return server
}

func TestRateLimitAllowedRequestCarriesRateLimitHeaders(t *testing.T) {
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{
		Allowed: true, Limit: 100, Remaining: 99, ResetAfter: 1500 * time.Millisecond,
	}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", recorder.Code)
	}
	expected := map[string]string{"RateLimit-Limit": "100", "RateLimit-Remaining": "99", "RateLimit-Reset": "2"}
	for header, want := range expected {
		if got := recorder.Header().Get(header); got != want {
			t.Fatalf("%s = %q, want %q", header, got, want)
		}
	}
	if got := recorder.Header().Get("Retry-After"); got != "" {
		t.Fatalf("Retry-After = %q, want empty on an allowed request", got)
	}
}

func TestRateLimitLimitedRequestReturns429ProblemWithRetryAfter(t *testing.T) {
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{
		Allowed: false, Limit: 100, Remaining: 0, ResetAfter: 30 * time.Second, RetryAfter: 2100 * time.Millisecond,
	}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

	if recorder.Code != http.StatusTooManyRequests {
		t.Fatalf("status = %d, want 429", recorder.Code)
	}
	if got := recorder.Header().Get("Content-Type"); got != "application/problem+json" {
		t.Fatalf("Content-Type = %q, want application/problem+json", got)
	}
	if got := recorder.Header().Get("Retry-After"); got != "3" {
		t.Fatalf("Retry-After = %q, want 3", got)
	}
	if got := recorder.Header().Get("RateLimit-Remaining"); got != "0" {
		t.Fatalf("RateLimit-Remaining = %q, want 0", got)
	}
	var body struct {
		Title  string `json:"title"`
		Status int    `json:"status"`
	}
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("decode body: %v", err)
	}
	if body.Status != http.StatusTooManyRequests || body.Title != "Too Many Requests" {
		t.Fatalf("body = %+v, want status 429 and title Too Many Requests", body)
	}
}

func TestRateLimitFailsOpenWhenTheLimiterErrors(t *testing.T) {
	logBuffer := &bytes.Buffer{}
	limiter := &fakeLimiter{err: errors.New("connection refused")}
	server := rateLimitedServer(t, logBuffer, httpserver.RateLimitSettings{Limiter: limiter})

	for range 3 {
		recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))
		if recorder.Code != http.StatusOK {
			t.Fatalf("status = %d, want 200 (fail open)", recorder.Code)
		}
		if got := recorder.Header().Get("RateLimit-Limit"); got != "" {
			t.Fatalf("RateLimit-Limit = %q, want no rate limit headers without a decision", got)
		}
	}
	if count := strings.Count(logBuffer.String(), "rate limiter unavailable"); count != 1 {
		t.Fatalf("logged the limiter error %d times, want exactly 1 (rate limited warning)", count)
	}
}

func TestRateLimitSkipsRequestsOutsideTheVersionedAPI(t *testing.T) {
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{Allowed: false, Limit: 1}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

	for _, path := range []string{"/health/live", "/health/ready", "/docs", "/openapi.json"} {
		recorder := serve(server, httptest.NewRequest(http.MethodGet, path, nil))
		if recorder.Code == http.StatusTooManyRequests {
			t.Fatalf("%s was rate limited", path)
		}
	}
	if keys := limiter.calledKeys(); len(keys) != 0 {
		t.Fatalf("limiter was consulted for excluded paths: %v", keys)
	}
}

func TestRateLimitSkipsPreflightRequests(t *testing.T) {
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{Allowed: false, Limit: 1}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})
	request := httptest.NewRequest(http.MethodOptions, "/v1/ping", nil)
	request.Header.Set("Origin", allowedTestOrigin)
	request.Header.Set("Access-Control-Request-Method", http.MethodGet)

	recorder := serve(server, request)

	if recorder.Code == http.StatusTooManyRequests || len(limiter.calledKeys()) != 0 {
		t.Fatalf("preflight was rate limited (status %d, keys %v)", recorder.Code, limiter.calledKeys())
	}
}

func TestRateLimitIsDisabledWithoutALimiter(t *testing.T) {
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{})

	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

	if recorder.Code != http.StatusOK || recorder.Header().Get("RateLimit-Limit") != "" {
		t.Fatalf("status = %d, RateLimit-Limit = %q; want 200 and no headers", recorder.Code, recorder.Header().Get("RateLimit-Limit"))
	}
}

func TestRateLimitUsesACustomKeyFunction(t *testing.T) {
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{Allowed: true, Limit: 1, Remaining: 1}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{
		Limiter:     limiter,
		KeyFunction: func(*http.Request) string { return "tenant:42" },
	})

	serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

	if keys := limiter.calledKeys(); len(keys) != 1 || keys[0] != "tenant:42" {
		t.Fatalf("keys = %v, want [tenant:42]", keys)
	}
}

func TestClientAddressKeyFunction(t *testing.T) {
	trusted := []netip.Prefix{netip.MustParsePrefix("10.0.0.0/8")}
	cases := []struct {
		name          string
		remoteAddress string
		forwardedFor  string
		want          string
	}{
		{"direct client ignores forwarded header", "203.0.113.7:5000", "198.51.100.1", "ip:203.0.113.7"},
		{"trusted proxy uses forwarded client", "10.0.0.2:5000", "198.51.100.1", "ip:198.51.100.1"},
		{"trusted proxy chain skips trusted hops", "10.0.0.2:5000", "198.51.100.1, 10.0.0.9", "ip:198.51.100.1"},
		{"spoofed leftmost entry is ignored", "10.0.0.2:5000", "1.1.1.1, 198.51.100.1", "ip:198.51.100.1"},
		{"trusted proxy without header uses remote", "10.0.0.2:5000", "", "ip:10.0.0.2"},
		{"garbage forwarded entry falls back to remote", "10.0.0.2:5000", "not-an-ip", "ip:10.0.0.2"},
	}
	keyFunction := httpserver.ClientAddressKey(trusted)
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			request := httptest.NewRequest(http.MethodGet, "/v1/ping", nil)
			request.RemoteAddr = testCase.remoteAddress
			if testCase.forwardedFor != "" {
				request.Header.Set("X-Forwarded-For", testCase.forwardedFor)
			}
			if got := keyFunction(request); got != testCase.want {
				t.Fatalf("key = %q, want %q", got, testCase.want)
			}
		})
	}
}
