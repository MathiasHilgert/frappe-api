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
	openAPI.Paths = map[string]*huma.PathItem{
		"/v1/time_zones/{id...}": {Get: &huma.Operation{Parameters: []*huma.Param{{Name: "id", In: "path"}}}},
		"/v1/geo/cities": {Get: &huma.Operation{Parameters: []*huma.Param{
			{Name: "country_code", In: "query"}, {Name: "expand[]", In: "query"}, {Name: "If-None-Match", In: "header"},
		}}},
	}
	if err := rest.CheckNaming(openAPI); err != nil {
		t.Fatalf("CheckNaming error = %v", err)
	}
}

func TestCheckNamingRejectsOtherCases(t *testing.T) {
	openAPI := openAPIWith(camelBody{})
	openAPI.Paths = map[string]*huma.PathItem{
		"/v1/timeZones": {},
		"/v1/dishes/{dishId}": {Get: &huma.Operation{Parameters: []*huma.Param{
			{Name: "dishId", In: "path"}, {Name: "countryCode", In: "query"}, {Name: "filter[]", In: "query"},
		}}},
	}
	err := rest.CheckNaming(openAPI)
	if err == nil {
		t.Fatal("CheckNaming accepted non snake_case names")
	}
	for _, name := range []string{"createdAt", "timeZones", "dishId", "countryCode", "filter[]"} {
		if !strings.Contains(err.Error(), `"`+name+`"`) {
			t.Fatalf("CheckNaming error = %v, want a violation naming %s", err, name)
		}
	}
}
