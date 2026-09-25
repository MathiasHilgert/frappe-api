package rest_test

import (
	"reflect"
	"strings"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

type snakeBody struct {
	Object    string `json:"object"`
	CreatedAt string `json:"created_at"`
}

type camelBody struct {
	CreatedAt string `json:"createdAt"`
}

func openAPIWith(values ...any) *huma.OpenAPI {
	registry := huma.NewMapRegistry("#/components/schemas/", huma.DefaultSchemaNamer)
	for _, value := range values {
		registry.Schema(reflect.TypeOf(value), true, "")
	}
	return &huma.OpenAPI{Components: &huma.Components{Schemas: registry}}
}

func TestCheckNamingAcceptsSnakeCase(t *testing.T) {
	openAPI := openAPIWith(snakeBody{}, rest.List[snakeBody]{})
	openAPI.Paths = map[string]*huma.PathItem{"/v1/time_zones/{id...}": {}, "/v1/geo/cities": {}}
	if err := rest.CheckNaming(openAPI); err != nil {
		t.Fatalf("CheckNaming error = %v", err)
	}
}

func TestCheckNamingRejectsOtherCases(t *testing.T) {
	openAPI := openAPIWith(camelBody{})
	openAPI.Paths = map[string]*huma.PathItem{"/v1/timeZones": {}}
	err := rest.CheckNaming(openAPI)
	if err == nil || !strings.Contains(err.Error(), "createdAt") || !strings.Contains(err.Error(), "timeZones") {
		t.Fatalf("CheckNaming error = %v, want violations naming createdAt and timeZones", err)
	}
}
