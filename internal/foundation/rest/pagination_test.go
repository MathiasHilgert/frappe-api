package rest_test

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"net/url"
	"reflect"
	"strconv"
	"strings"
	"testing"

	"github.com/danielgtaylor/huma/v2"
	"github.com/danielgtaylor/huma/v2/humatest"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

type listInput struct {
	Country string `query:"country"`
	rest.ExpandParameters
	rest.PageParameters
}

// catalog is 25 dishes ordered by id, standing in for a repository.
func catalog() []dish {
	dishes := make([]dish, 0, 25)
	for index := range 25 {
		dishes = append(dishes, dish{Object: "dish", ID: "dish_" + string(rune('a'+index))})
	}
	return dishes
}

func paginatedAPI(t *testing.T) humatest.TestAPI {
	t.Helper()
	_, api := humatest.New(t)
	codec := newCodec(t)
	expansions := rest.NewExpansions("country")
	huma.Get(api, "/dishes", func(_ context.Context, input *listInput) (*rest.ListOutput[dish], error) {
		if _, err := expansions.Parse(input.Expand); err != nil {
			return nil, err
		}
		var after position
		found, err := input.Position(codec, "dishes", &after)
		if err != nil {
			return nil, err
		}
		rows := make([]dish, 0, input.Limit+1)
		for _, candidate := range catalog() {
			if (!found || candidate.ID > after.ID) && len(rows) <= input.Limit {
				rows = append(rows, candidate)
			}
		}
		return rest.NewPage(codec, "dishes", input.PageParameters, rows, func(last dish) any { return position{ID: last.ID} })
	})
	return api
}

func decodeList(t *testing.T, response *httptest.ResponseRecorder) rest.List[dish] {
	t.Helper()
	if response.Code != http.StatusOK {
		t.Fatalf("status = %d, body %s", response.Code, response.Body)
	}
	var list rest.List[dish]
	if err := json.Unmarshal(response.Body.Bytes(), &list); err != nil {
		t.Fatal(err)
	}
	return list
}

func TestPaginationWalksEveryPageWithTheDefaultLimit(t *testing.T) {
	api := paginatedAPI(t)
	first := decodeList(t, api.Get("/dishes"))
	if len(first.Data) != rest.DefaultLimit || !first.HasMore || first.NextCursor == nil {
		t.Fatalf("first page = %d items, has_more %v", len(first.Data), first.HasMore)
	}
	if first.URL != "/dishes" {
		t.Fatalf("url = %q, want /dishes", first.URL)
	}
	seen := len(first.Data)
	cursor := *first.NextCursor
	for {
		page := decodeList(t, api.Get("/dishes?cursor="+url.QueryEscape(cursor)))
		seen += len(page.Data)
		if !page.HasMore {
			if page.NextCursor != nil {
				t.Fatal("last page carries a next_cursor")
			}
			break
		}
		cursor = *page.NextCursor
	}
	if seen != 25 {
		t.Fatalf("walked %d items, want 25", seen)
	}
}

func TestPaginationSendsAnRFC8288NextLinkKeepingFilters(t *testing.T) {
	api := paginatedAPI(t)
	response := api.Get("/dishes?country=AR&limit=5")
	list := decodeList(t, response)
	link := response.Header().Get("Link")
	want := `</dishes?country=AR&cursor=` + url.QueryEscape(*list.NextCursor) + `&limit=5>; rel="next"`
	if link != want {
		t.Fatalf("Link = %q\nwant %q", link, want)
	}

	last := api.Get("/dishes?limit=100")
	if got := last.Header().Get("Link"); got != "" {
		t.Fatalf("last page Link = %q, want none", got)
	}
}

func TestPaginationRejectsInvalidInput(t *testing.T) {
	api := paginatedAPI(t)
	cases := map[string]struct {
		path   string
		status int
	}{
		"limit zero":        {"/dishes?limit=0", http.StatusUnprocessableEntity},
		"limit above max":   {"/dishes?limit=101", http.StatusUnprocessableEntity},
		"tampered cursor":   {"/dishes?cursor=abc.def", http.StatusBadRequest},
		"unknown expansion": {"/dishes?expand[]=owner", http.StatusBadRequest},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			response := api.Get(testCase.path)
			if response.Code != testCase.status {
				t.Fatalf("status = %d, want %d, body %s", response.Code, testCase.status, response.Body)
			}
			if !strings.HasPrefix(response.Header().Get("Content-Type"), "application/problem+json") {
				t.Fatalf("content type = %q, want application/problem+json", response.Header().Get("Content-Type"))
			}
		})
	}
}

func TestPageParametersTagsMatchTheLimitConstants(t *testing.T) {
	field, _ := reflect.TypeFor[rest.PageParameters]().FieldByName("Limit")
	if got := field.Tag.Get("default"); got != strconv.Itoa(rest.DefaultLimit) {
		t.Fatalf("default tag = %q, want %d", got, rest.DefaultLimit)
	}
	if got := field.Tag.Get("maximum"); got != strconv.Itoa(rest.MaximumLimit) {
		t.Fatalf("maximum tag = %q, want %d", got, rest.MaximumLimit)
	}
	expand, _ := reflect.TypeFor[rest.ExpandParameters]().FieldByName("Expand")
	if got := expand.Tag.Get("maxItems"); got != strconv.Itoa(rest.MaximumExpansions) {
		t.Fatalf("maxItems tag = %q, want %d", got, rest.MaximumExpansions)
	}
}

func TestExpandAcceptsRepeatedBracketParameters(t *testing.T) {
	api := paginatedAPI(t)
	if response := api.Get("/dishes?expand[]=country&expand[]=country"); response.Code != http.StatusOK {
		t.Fatalf("status = %d, body %s", response.Code, response.Body)
	}
}
