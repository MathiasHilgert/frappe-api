package usecase

import "testing"

func TestHandlerNameFollowsModuleKindAndType(t *testing.T) {
	t.Parallel()
	cases := []struct {
		packagePath, typeName, want string
	}{
		{"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query", "GetCountryHandler", "geo.query.get_country"},
		{"github.com/MathiasHilgert/frappe-api/internal/modules/menu/application/command", "CreateDishHandler", "menu.command.create_dish"},
		{"example.com/usecase_test", "GetThingHandler", "usecase_test.get_thing"},
		{"example.com/query", "ListHandler[int]", "query.list"},
		{"example.com/query", "GetHTTPStatus", "query.get_http_status"},
	}
	for _, testCase := range cases {
		if got := (handlerName{}).of(testCase.packagePath, testCase.typeName); got != testCase.want {
			t.Errorf("of(%q, %q) = %q, want %q", testCase.packagePath, testCase.typeName, got, testCase.want)
		}
	}
}
