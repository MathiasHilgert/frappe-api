package http_test

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"testing"

	"github.com/danielgtaylor/huma/v2"
	"github.com/danielgtaylor/huma/v2/adapters/humago"
	"github.com/danielgtaylor/huma/v2/humatest"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
)

// fakeQuery is a query handler returning result and err, recording what
// it received.
type fakeQuery[Query, Result any] struct {
	result   Result
	err      error
	received []Query
}

func (fake *fakeQuery[Query, Result]) Handle(_ context.Context, input Query) (Result, error) {
	fake.received = append(fake.received, input)
	return fake.result, fake.err
}

// testAPI serves handlers on a bare huma API, without the /v1 middleware
// chain (no localization, no caching).
type testAPI struct {
	t      *testing.T
	api    humatest.TestAPI
	shared httpadapter.Shared
}

func newTestAPI(t *testing.T) testAPI {
	t.Helper()
	// The go 1.22 ServeMux adapter, like the real server, so {id...}
	// wildcards route as in production.
	api := humatest.Wrap(t, humago.New(http.NewServeMux(), huma.DefaultConfig("geo", "0.0.0")))
	cursors, err := rest.NewCursorCodec(bytes.Repeat([]byte("s"), rest.MinimumCursorSecretBytes))
	if err != nil {
		t.Fatalf("cursor codec: %v", err)
	}
	revision := &fakeQuery[query.GetDataRevision, string]{result: "geo-seed-3"}
	return testAPI{t: t, api: api, shared: httpadapter.Shared{Revision: revision, Cursors: cursors}}
}

// get requests target and decodes its JSON object body.
func (api testAPI) get(target string, status int) map[string]any {
	api.t.Helper()
	recorder := api.api.Get(target)
	if recorder.Code != status {
		api.t.Fatalf("GET %s = %d, want %d: %s", target, recorder.Code, status, recorder.Body.String())
	}
	body := map[string]any{}
	if recorder.Code != http.StatusNoContent && recorder.Body.Len() > 0 {
		if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
			api.t.Fatalf("GET %s body %q: %v", target, recorder.Body.String(), err)
		}
	}
	return body
}

// items returns the data of a list body.
func (api testAPI) items(body map[string]any) []map[string]any {
	api.t.Helper()
	data, ok := body["data"].([]any)
	if !ok {
		api.t.Fatalf("body has no data array: %v", body)
	}
	items := make([]map[string]any, 0, len(data))
	for _, item := range data {
		items = append(items, item.(map[string]any))
	}
	return items
}
