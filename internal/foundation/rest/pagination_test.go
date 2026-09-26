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

// menuInput repeats listInput's fields: Huma only binds parameters of
// directly embedded structs, not of structs embedded two levels deep.
type menuInput struct {
	Name    string `path:"name"`
	Country string `query:"country"`
	OrderBy string `query:"order_by"`
	rest.ExpandParameters
	rest.PageParameters
}

type listInput struct {
	Country string `query:"country"`
	OrderBy string `query:"order_by"`
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
	handler := func(ctx context.Context, input *listInput) (*rest.ListOutput[dish], error) {
		if _, err := expansions.Parse(ctx, input.Expand); err != nil {
			return nil, err
		}
		var after position
		found, err := input.Position(ctx, codec, &after)
		if err != nil {
			return nil, err
		}
		rows := make([]dish, 0, input.Limit+1)
		for _, candidate := range catalog() {
			if (!found || candidate.ID > after.ID) && len(rows) <= input.Limit {
				rows = append(rows, candidate)
			}
		}
		return rest.NewPage(codec, input.PageParameters, rows, func(last dish) any { return position{ID: last.ID} })
	}
	huma.Get(api, "/dishes", handler)
	huma.Get(api, "/menus/{name}/dishes", func(ctx context.Context, input *menuInput) (*rest.ListOutput[dish], error) {
		return handler(ctx, &listInput{Country: input.Country, OrderBy: input.OrderBy, ExpandParameters: input.ExpandParameters, PageParameters: input.PageParameters})
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
		"unknown expansion": {"/dishes?expand[]=owner", http.StatusUnprocessableEntity},
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

func firstCursor(t *testing.T, api humatest.TestAPI, path string) string {
	t.Helper()
	list := decodeList(t, api.Get(path))
	if list.NextCursor == nil {
		t.Fatalf("%s returned no next_cursor", path)
	}
	return url.QueryEscape(*list.NextCursor)
}

func TestCursorIsBoundToTheOrderAndFilters(t *testing.T) {
	api := paginatedAPI(t)
	cursor := firstCursor(t, api, "/dishes?order_by=-created_at&country=AR")
	cases := map[string]struct {
		path   string
		status int
	}{
		"other order":            {"/dishes?order_by=name&country=AR&cursor=" + cursor, http.StatusBadRequest},
		"other filter":           {"/dishes?order_by=-created_at&country=UY&cursor=" + cursor, http.StatusBadRequest},
		"dropped filter":         {"/dishes?order_by=-created_at&cursor=" + cursor, http.StatusBadRequest},
		"other collection":       {"/menus/lunch/dishes?order_by=-created_at&country=AR&cursor=" + cursor, http.StatusBadRequest},
		"same query reordered":   {"/dishes?cursor=" + cursor + "&country=AR&order_by=-created_at", http.StatusOK},
		"other limit and expand": {"/dishes?country=AR&order_by=-created_at&limit=3&expand[]=country&cursor=" + cursor, http.StatusOK},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			if response := api.Get(testCase.path); response.Code != testCase.status {
				t.Fatalf("status = %d, want %d, body %s", response.Code, testCase.status, response.Body)
			}
		})
	}
}

func TestLinkAndURLKeepThePathEscaped(t *testing.T) {
	api := paginatedAPI(t)
	response := api.Get("/menus/a%3Eb%2Cc%3Bd%20e/dishes?limit=5")
	list := decodeList(t, response)
	escapedPath := "/menus/a%3Eb%2Cc%3Bd%20e/dishes"
	if list.URL != escapedPath {
		t.Fatalf("url = %q, want %q", list.URL, escapedPath)
	}
	want := "<" + escapedPath + "?cursor=" + url.QueryEscape(*list.NextCursor) + `&limit=5>; rel="next"`
	if got := response.Header().Get("Link"); got != want {
		t.Fatalf("Link = %q\nwant %q", got, want)
	}
	if next := api.Get(strings.TrimSuffix(strings.TrimPrefix(want, "<"), `>; rel="next"`)); next.Code != http.StatusOK {
		t.Fatalf("following Link: status = %d, body %s", next.Code, next.Body)
	}
}

func TestNewPageTreatsAMissingLimitAsTheDefault(t *testing.T) {
	codec := newCodec(t)
	output, err := rest.NewPage(codec, rest.PageParameters{}, catalog(), func(last dish) any { return position{ID: last.ID} })
	if err != nil {
		t.Fatal(err)
	}
	if len(output.Body.Data) != rest.DefaultLimit || !output.Body.HasMore {
		t.Fatalf("page = %d items, has_more %v, want %d and true", len(output.Body.Data), output.Body.HasMore, rest.DefaultLimit)
	}
}
