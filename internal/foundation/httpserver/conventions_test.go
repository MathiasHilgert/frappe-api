package httpserver_test

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

type timeZoneInput struct {
	ID string `path:"id"`
}

type timeZoneOutput struct {
	Body struct {
		Object string `json:"object"`
		ID     string `json:"id"`
	}
}

func conventionsServer(t *testing.T) *httpserver.Server {
	t.Helper()
	server := httpserver.New(testSettings(nil, func() bool { return true }, true))
	huma.Get(server.V1(), "/time_zones/{id...}", func(_ context.Context, input *timeZoneInput) (*timeZoneOutput, error) {
		output := &timeZoneOutput{}
		output.Body.Object = "time_zone"
		output.Body.ID = input.ID
		return output, nil
	})
	return server
}

func TestResponsesCarryNoSchemaLinkField(t *testing.T) {
	response := httptest.NewRecorder()
	conventionsServer(t).Handler().ServeHTTP(response, httptest.NewRequest(http.MethodGet, "/v1/time_zones/UTC", nil))
	want := `{"object":"time_zone","id":"UTC"}` + "\n"
	if response.Body.String() != want {
		t.Fatalf("body = %q, want %q (no $schema field)", response.Body.String(), want)
	}
	if link := response.Header().Get("Link"); link != "" {
		t.Fatalf("Link = %q, want no describedby schema link", link)
	}
}

func TestTrailingWildcardPathParametersKeepSlashes(t *testing.T) {
	response := httptest.NewRecorder()
	conventionsServer(t).Handler().ServeHTTP(response, httptest.NewRequest(http.MethodGet, "/v1/time_zones/America/Argentina/Cordoba", nil))
	want := `{"object":"time_zone","id":"America/Argentina/Cordoba"}` + "\n"
	if response.Code != http.StatusOK || response.Body.String() != want {
		t.Fatalf("status = %d, body = %q, want 200 %q", response.Code, response.Body.String(), want)
	}
}
