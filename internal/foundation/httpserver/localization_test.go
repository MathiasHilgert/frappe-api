package httpserver_test

import (
	"context"
	"net/http"
	"net/http/httptest"
	"slices"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

type localeKey struct{}

// fakeLocalization stands in for i18n.Catalog.Middleware: it stores a
// fixed locale and sets the headers the real one sets.
func fakeLocalization(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Language", "en")
		w.Header().Add("Vary", "Accept-Language")
		next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), localeKey{}, "en")))
	})
}

func TestLocalizationMiddlewareWrapsTheV1API(t *testing.T) {
	settings := testSettings(nil, func() bool { return true }, false)
	settings.Localization = fakeLocalization
	server := httpserver.New(settings)

	type output struct {
		Body struct {
			Locale string `json:"locale"`
		}
	}
	var seen string
	huma.Get(server.V1(), "/things", func(ctx context.Context, _ *struct{}) (*output, error) {
		seen, _ = ctx.Value(localeKey{}).(string)
		return &output{}, nil
	})

	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/things", nil))
	if seen != "en" {
		t.Fatalf("handler locale = %q, want en", seen)
	}
	if got := recorder.Header().Get("Content-Language"); got != "en" {
		t.Fatalf("Content-Language = %q, want en", got)
	}
	if !slices.Contains(recorder.Header().Values("Vary"), "Accept-Language") {
		t.Fatalf("Vary = %v, want Accept-Language kept next to the caching Vary", recorder.Header().Values("Vary"))
	}
}

func TestLocalizationIsOptional(t *testing.T) {
	server := httpserver.New(testSettings(nil, func() bool { return true }, false))
	huma.Get(server.V1(), "/things", func(context.Context, *struct{}) (*struct{}, error) { return nil, nil })
	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/things", nil))
	if got := recorder.Header().Get("Content-Language"); got != "" {
		t.Fatalf("Content-Language = %q, want none", got)
	}
}

func TestRecoveredPanicDropsContentLanguage(t *testing.T) {
	settings := testSettings(nil, func() bool { return true }, false)
	settings.Localization = fakeLocalization
	server := httpserver.New(settings)
	huma.Get(server.V1(), "/boom", func(context.Context, *struct{}) (*struct{}, error) { panic("boom") })

	recorder := serve(server, httptest.NewRequest(http.MethodGet, "/v1/boom", nil))
	if recorder.Code != http.StatusInternalServerError {
		t.Fatalf("status = %d, want 500", recorder.Code)
	}
	if got := recorder.Header().Get("Content-Language"); got != "" {
		t.Fatalf("Content-Language = %q on an unlocalized 500, want none", got)
	}
}
