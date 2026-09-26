//go:build integration

package http_test

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"

	foundation "github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo"
)

type readyPool struct{ pool *pgxpool.Pool }

func (source readyPool) Get() (*pgxpool.Pool, bool) { return source.pool, true }

// hooks collects the module's lifecycle hooks.
type hooks struct{ appended []foundation.Hook }

func (collected *hooks) Append(hook foundation.Hook) {
	collected.appended = append(collected.appended, hook)
}

// integrationAPI serves the whole geo module on a fresh seeded database
// through the real /v1 middleware chain (localization, caching), like the
// running API, after running its startup hooks.
type integrationAPI struct {
	t       *testing.T
	handler http.Handler
}

func newIntegrationAPI(t *testing.T) integrationAPI {
	t.Helper()
	catalog, err := i18n.NewCatalog(i18n.Settings{
		Messages:  i18n.EmbeddedMessages,
		Source:    i18n.MustParseLocale("es-419"),
		Supported: []i18n.Locale{i18n.MustParseLocale("es-419"), i18n.MustParseLocale("en"), i18n.MustParseLocale("pt-BR"), i18n.MustParseLocale("ja")},
	})
	if err != nil {
		t.Fatalf("catalog: %v", err)
	}
	server := httpserver.New(httpserver.Settings{
		Title: "frappe-api", Version: "0.0.0",
		ReadHeaderTimeout: time.Second, ReadTimeout: time.Second, WriteTimeout: time.Second, IdleTimeout: time.Second,
		MaxHeaderBytes: 1 << 20, MaxBodyBytes: 1 << 20,
		Logger:       slog.New(slog.NewTextHandler(io.Discard, nil)),
		Localization: catalog.Middleware,
	})
	cursors, err := rest.NewCursorCodec(bytes.Repeat([]byte("s"), rest.MinimumCursorSecretBytes))
	if err != nil {
		t.Fatalf("cursor codec: %v", err)
	}
	module := geo.New(geo.Dependencies{API: server.V1(), Pool: readyPool{pool: databasetest.New(t)}, Cursors: cursors})
	collected := &hooks{}
	if err := module.Register(collected); err != nil {
		t.Fatalf("register: %v", err)
	}
	for _, hook := range collected.appended {
		if err := hook.Up(context.Background()); err != nil {
			t.Fatalf("hook %s: %v", hook.Name, err)
		}
	}
	if err := rest.CheckNaming(server.V1().OpenAPI()); err != nil {
		t.Fatalf("naming: %v", err)
	}
	return integrationAPI{t: t, handler: server.Handler()}
}

type response struct {
	header http.Header
	body   map[string]any
	status int
}

// get requests target with header name/value pairs and expects status.
func (api integrationAPI) get(target string, status int, headers ...string) response {
	api.t.Helper()
	request := httptest.NewRequest(http.MethodGet, target, nil)
	for index := 0; index+1 < len(headers); index += 2 {
		request.Header.Set(headers[index], headers[index+1])
	}
	recorder := httptest.NewRecorder()
	api.handler.ServeHTTP(recorder, request)
	result := response{status: recorder.Code, header: recorder.Header()}
	if recorder.Body.Len() > 0 {
		if err := json.Unmarshal(recorder.Body.Bytes(), &result.body); err != nil {
			api.t.Fatalf("GET %s: body %q is not a JSON object: %v", target, recorder.Body.String(), err)
		}
	}
	if result.status != status {
		api.t.Fatalf("GET %s = %d, want %d: %v", target, result.status, status, result.body)
	}
	return result
}

func (api integrationAPI) items(result response) []map[string]any {
	api.t.Helper()
	data, ok := result.body["data"].([]any)
	if !ok {
		api.t.Fatalf("body has no data array: %v", result.body)
	}
	items := make([]map[string]any, 0, len(data))
	for _, item := range data {
		items = append(items, item.(map[string]any))
	}
	return items
}

// nextTarget is the Link rel="next" target of result, or "".
func (api integrationAPI) nextTarget(result response) string {
	api.t.Helper()
	link := result.header.Get("Link")
	if result.body["has_more"] != true {
		if link != "" || result.body["next_cursor"] != nil {
			api.t.Fatalf("last page Link = %q, next_cursor = %v", link, result.body["next_cursor"])
		}
		return ""
	}
	if !strings.HasSuffix(link, `>; rel="next"`) || result.body["next_cursor"] == nil {
		api.t.Fatalf("page has more but Link = %q, next_cursor = %v", link, result.body["next_cursor"])
	}
	return strings.TrimSuffix(strings.TrimPrefix(link, "<"), `>; rel="next"`)
}

// revalidates checks target is publicly cacheable, answers 304 to its own
// ETag and has another ETag in another language.
func (api integrationAPI) revalidates(target string) {
	api.t.Helper()
	first := api.get(target, http.StatusOK, "Accept-Language", "es-419")
	etag := first.header.Get("ETag")
	if etag == "" || first.header.Get("Cache-Control") != "public, max-age=3600, stale-while-revalidate=86400" {
		api.t.Fatalf("GET %s ETag = %q, Cache-Control = %q", target, etag, first.header.Get("Cache-Control"))
	}
	if revalidated := api.get(target, http.StatusNotModified, "Accept-Language", "es-419", "If-None-Match", etag); revalidated.header.Get("ETag") != etag {
		api.t.Fatalf("304 ETag = %q, want %q", revalidated.header.Get("ETag"), etag)
	}
	if other := api.get(target, http.StatusOK, "Accept-Language", "ja", "If-None-Match", etag); other.header.Get("ETag") == etag {
		api.t.Fatalf("GET %s has the same ETag in two languages", target)
	}
}

// notFound checks target is a 404 that no cache keeps.
func (api integrationAPI) notFound(target string) response {
	api.t.Helper()
	result := api.get(target, http.StatusNotFound)
	if result.header.Get("Cache-Control") != "no-store" {
		api.t.Fatalf("GET %s Cache-Control = %q, want no-store", target, result.header.Get("Cache-Control"))
	}
	return result
}
