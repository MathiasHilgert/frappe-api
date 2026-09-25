package httpserver_test

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

type cachedOutput struct {
	Body struct {
		Name string `json:"name"`
	}
}

// cachingServer builds a real Server whose /v1 API carries routes
// exercising each cache policy the way a module adapter would.
func cachingServer(t *testing.T, bodyComputations *int) *httpserver.Server {
	t.Helper()
	settings := testSettings(nil, func() bool { return true }, false)
	settings.CacheVaryHeaders = []string{"X-Tenant"}
	server := httpserver.New(settings)
	api := server.V1()

	register := func(path string, policy *httpserver.CachePolicy) {
		huma.Get(api, path, func(ctx context.Context, _ *struct{}) (*cachedOutput, error) {
			if policy != nil {
				if httpserver.NotModified(ctx, httpserver.ETagFromVersion("thing/1", 7), *policy) {
					return nil, huma.Status304NotModified()
				}
			}
			*bodyComputations++
			output := &cachedOutput{}
			output.Body.Name = "thing"
			return output, nil
		})
	}
	register("/default", nil)
	privatePolicy := httpserver.Private(time.Minute)
	register("/private", &privatePolicy)
	publicPolicy := httpserver.Public(time.Hour, 30*time.Second)
	register("/public", &publicPolicy)
	revalidatePolicy := httpserver.Revalidate()
	register("/revalidate", &revalidatePolicy)
	huma.Get(api, "/failing", func(ctx context.Context, _ *struct{}) (*cachedOutput, error) {
		httpserver.NotModified(ctx, httpserver.ETagFromVersion("thing/1", 7), httpserver.Public(time.Hour, 0))
		return nil, huma.Error404NotFound("missing")
	})
	return server
}

func serveCaching(server *httpserver.Server, method, path, ifNoneMatch string) *httptest.ResponseRecorder {
	request := httptest.NewRequest(method, path, nil)
	if ifNoneMatch != "" {
		request.Header.Set("If-None-Match", ifNoneMatch)
	}
	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)
	return recorder
}

func TestCachingDefaultsToNoStore(t *testing.T) {
	computations := 0
	recorder := serveCaching(cachingServer(t, &computations), http.MethodGet, "/v1/default", "")

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", recorder.Code)
	}
	if got := recorder.Header().Get("Cache-Control"); got != "no-store" {
		t.Fatalf("Cache-Control = %q, want no-store", got)
	}
	if got := recorder.Header().Get("ETag"); got != "" {
		t.Fatalf("ETag = %q, want none", got)
	}
}

func TestCachingErrorResponsesStayNoStoreEvenWithAPolicy(t *testing.T) {
	computations := 0
	recorder := serveCaching(cachingServer(t, &computations), http.MethodGet, "/v1/failing", "")

	if recorder.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want 404", recorder.Code)
	}
	if got := recorder.Header().Get("Cache-Control"); got != "no-store" {
		t.Fatalf("Cache-Control = %q, want no-store", got)
	}
	if got := recorder.Header().Get("ETag"); got != "" {
		t.Fatalf("ETag = %q, want none on an error", got)
	}
}

func TestCachingPoliciesSetDirectivesVaryAndETag(t *testing.T) {
	etag := httpserver.ETagFromVersion("thing/1", 7)
	cases := []struct {
		path         string
		cacheControl string
		vary         []string
	}{
		{path: "/v1/private", cacheControl: "private, max-age=60", vary: []string{"Authorization", "X-Tenant"}},
		{path: "/v1/public", cacheControl: "public, max-age=3600, stale-while-revalidate=30", vary: nil},
		{path: "/v1/revalidate", cacheControl: "no-cache", vary: []string{"Authorization", "X-Tenant"}},
	}
	for _, testCase := range cases {
		t.Run(testCase.path, func(t *testing.T) {
			computations := 0
			recorder := serveCaching(cachingServer(t, &computations), http.MethodGet, testCase.path, `"other"`)

			if recorder.Code != http.StatusOK || recorder.Body.Len() == 0 {
				t.Fatalf("status = %d body = %q, want 200 with a body", recorder.Code, recorder.Body.String())
			}
			if got := recorder.Header().Get("Cache-Control"); got != testCase.cacheControl {
				t.Fatalf("Cache-Control = %q, want %q", got, testCase.cacheControl)
			}
			if got := recorder.Header().Values("Vary"); len(got) != len(testCase.vary) || (len(got) > 0 && (got[0] != testCase.vary[0] || got[1] != testCase.vary[1])) {
				t.Fatalf("Vary = %v, want %v", got, testCase.vary)
			}
			if got := recorder.Header().Get("ETag"); got != etag {
				t.Fatalf("ETag = %q, want %q", got, etag)
			}
		})
	}
}

func TestCachingMatchingIfNoneMatchReturns304WithoutBody(t *testing.T) {
	etag := httpserver.ETagFromVersion("thing/1", 7)
	for _, method := range []string{http.MethodGet, http.MethodHead} {
		t.Run(method, func(t *testing.T) {
			reader := rateLimitMetricReader(t)
			computations := 0
			recorder := serveCaching(cachingServer(t, &computations), method, "/v1/private", `"x", W/`+etag)

			if recorder.Code != http.StatusNotModified {
				t.Fatalf("status = %d, want 304", recorder.Code)
			}
			if recorder.Body.Len() != 0 {
				t.Fatalf("body = %q, want empty", recorder.Body.String())
			}
			if computations != 0 {
				t.Fatalf("body computed %d times, want 0", computations)
			}
			if got := recorder.Header().Get("ETag"); got != etag {
				t.Fatalf("ETag = %q, want %q", got, etag)
			}
			if got := recorder.Header().Get("Cache-Control"); got != "private, max-age=60" {
				t.Fatalf("Cache-Control = %q", got)
			}
			if got := recorder.Header().Get("Content-Type"); got != "" {
				t.Fatalf("Content-Type = %q, want none on 304", got)
			}
			point := singleCounterPoint(t, collectMetrics(t, reader), "frappe.http.not_modified")
			assertAttributes(t, "frappe.http.not_modified", point.Attributes, map[string]string{"http.route": "/v1/private"})
			if point.Value != 1 {
				t.Fatalf("not_modified = %d, want 1", point.Value)
			}
		})
	}
}

func TestCachingHeadWithoutConditionReturns200WithETag(t *testing.T) {
	computations := 0
	recorder := serveCaching(cachingServer(t, &computations), http.MethodHead, "/v1/private", "")

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", recorder.Code)
	}
	if got := recorder.Header().Get("ETag"); got == "" {
		t.Fatal("ETag missing on HEAD")
	}
}

func TestETagConstructorsProduceQuotedStrongTags(t *testing.T) {
	version := httpserver.ETagFromVersion("thing/1", 7)
	if version != httpserver.ETagFromVersion("thing/1", 7) || version == httpserver.ETagFromVersion("thing/1", 8) ||
		version == httpserver.ETagFromVersion("thing/2", 7) {
		t.Fatalf("ETagFromVersion is not deterministic and distinct: %q", version)
	}
	body := httpserver.ETagFromBody([]byte("hello"))
	if body != `"LPJNul-wow4m6DsqxbninhsWHlwfp0JecwQzYpOLmCQ"` {
		t.Fatalf("ETagFromBody = %q", body)
	}
	if version[0] != '"' || version[len(version)-1] != '"' {
		t.Fatalf("ETagFromVersion = %q, want quoted", version)
	}
}
