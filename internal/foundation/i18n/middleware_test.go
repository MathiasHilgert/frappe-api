package i18n_test

import (
	"net/http"
	"net/http/httptest"
	"slices"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

func TestMiddlewareNegotiatesAndSetsHeaders(t *testing.T) {
	catalog := testCatalog(t)
	var seen string
	handler := catalog.Middleware(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		locale, _ := i18n.FromContext(r.Context())
		seen = locale.String()
		_, _ = w.Write([]byte(i18n.TranslateWith(r.Context(), "greeting", i18n.Data{"Name": "X"})))
	}))

	request := httptest.NewRequest(http.MethodGet, "/v1/anything", nil)
	request.Header.Set("Accept-Language", "en-GB,en;q=0.9")
	recorder := httptest.NewRecorder()
	handler.ServeHTTP(recorder, request)

	if seen != "en" {
		t.Fatalf("locale in context = %q, want en", seen)
	}
	if got := recorder.Header().Get("Content-Language"); got != "en" {
		t.Fatalf("Content-Language = %q, want en", got)
	}
	if !slices.Contains(recorder.Header().Values("Vary"), "Accept-Language") {
		t.Fatalf("Vary = %v, want Accept-Language", recorder.Header().Values("Vary"))
	}
	if body := recorder.Body.String(); body != "Hello X" {
		t.Fatalf("body = %q", body)
	}
}

func TestMiddlewareDefaultsToSource(t *testing.T) {
	catalog := testCatalog(t)
	handler := catalog.Middleware(http.HandlerFunc(func(http.ResponseWriter, *http.Request) {}))
	recorder := httptest.NewRecorder()
	handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, "/", nil))
	if got := recorder.Header().Get("Content-Language"); got != "es-419" {
		t.Fatalf("Content-Language = %q, want es-419", got)
	}
}
