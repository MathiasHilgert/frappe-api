package rest

import (
	"net/url"
	"slices"
	"strings"

	"github.com/danielgtaylor/huma/v2"
)

const (
	// DefaultLimit is the page size when a request sends no limit.
	DefaultLimit = 10
	// MaximumLimit is the largest page size a request may ask for.
	MaximumLimit = 100
)

// PageParameters are the cursor pagination query parameters. Embed them
// in a list operation's input struct:
//
//	type ListCitiesInput struct {
//		rest.PageParameters
//		Country string `query:"country"`
//	}
//
// Huma validates limit (1 to 100, default 10) and answers 422 otherwise;
// Position answers 400 for a cursor this API did not issue for this
// listing. The struct tags repeat DefaultLimit and MaximumLimit because
// Go struct tags cannot reference constants; a test keeps them in sync.
type PageParameters struct {
	query url.Values
	// Cursor is the next_cursor of the previous page, or empty for the
	// first page.
	Cursor string `query:"cursor" maxLength:"1024" doc:"Opaque cursor from a previous page's next_cursor. Omit for the first page."`
	path   string
	// Limit is the page size.
	Limit int `query:"limit" default:"10" minimum:"1" maximum:"100" doc:"Page size, 1 to 100."`
}

// Resolve captures the request path, still escaped, and query, so NewPage
// can report the collection URL, build the Link header and derive the
// cursor scope. Huma calls it after binding.
func (parameters *PageParameters) Resolve(ctx huma.Context) []error {
	requestURL := ctx.URL()
	parameters.path = requestURL.EscapedPath()
	parameters.query = requestURL.Query()
	return nil
}

// pageParameterNames are the query parameters that do not change which
// rows a listing returns, or in which order, so they are left out of the
// cursor scope: a client may change the page size or expansions between
// pages.
var pageParameterNames = []string{"cursor", "limit", "expand[]"}

// principal is the scope slot for the authenticated principal and
// tenant. It is empty until authentication exists; then a cursor issued
// to one principal will not work for another.
const principal = ""

// scope derives the cursor scope from the request: the principal slot,
// the escaped path and the canonical query (keys and each key's values
// sorted, without pageParameterNames). Every filter and order_by is
// therefore part of it, so a cursor only works on the exact listing that
// issued it, whatever order the client writes the parameters in.
func (parameters PageParameters) scope() string {
	canonical := url.Values{}
	for key, values := range parameters.query {
		if slices.Contains(pageParameterNames, key) {
			continue
		}
		sorted := slices.Clone(values)
		slices.Sort(sorted)
		canonical[key] = sorted
	}
	return strings.Join([]string{"principal=" + url.QueryEscape(principal), parameters.path, canonical.Encode()}, "|")
}

// Position decodes the request cursor into target. found is false for a
// first page request (no cursor). A malformed cursor, or one issued for
// another listing (principal, path, filters or order), becomes a 400
// problem naming query.cursor, never echoing its value.
func (parameters PageParameters) Position(codec *CursorCodec, target any) (found bool, err error) {
	if parameters.Cursor == "" {
		return false, nil
	}
	if err := codec.Decode(parameters.Cursor, parameters.scope(), target); err != nil {
		return false, huma.Error400BadRequest("The pagination cursor is invalid or belongs to another listing. Restart from the first page.",
			&huma.ErrorDetail{Location: "query.cursor", Message: "invalid cursor"})
	}
	return true, nil
}

// NewPage builds the output of a paginated operation from rows, which the
// handler fetched with LIMIT parameters.Limit+1: the extra row only tells
// whether a next page exists and is never returned. positionOf returns
// the keyset position (the sort key values) of a row, which becomes the
// next cursor when there is a next page.
func NewPage[T any](codec *CursorCodec, parameters PageParameters, rows []T, positionOf func(T) any) (*ListOutput[T], error) {
	if parameters.Limit < 1 {
		// Only reachable when parameters did not go through Huma, which
		// applies the default and rejects anything below 1.
		parameters.Limit = DefaultLimit
	}
	output := &ListOutput[T]{}
	if len(rows) <= parameters.Limit {
		output.Body = NewList(parameters.path, rows, "")
		return output, nil
	}

	rows = rows[:parameters.Limit]
	cursor, err := codec.Encode(parameters.scope(), positionOf(rows[len(rows)-1]))
	if err != nil {
		return nil, err
	}
	output.Body = NewList(parameters.path, rows, cursor)
	output.Link = nextLink(parameters, cursor)
	return output, nil
}

// nextLink returns the RFC 8288 Link header value for the next page: the
// request's own path and query (filters, limit, expand[]) with cursor
// replaced. The target is a relative reference, which RFC 8288 section
// 3.1 resolves against the request URL.
func nextLink(parameters PageParameters, cursor string) string {
	query := url.Values{}
	for key, values := range parameters.query {
		query[key] = values
	}
	query.Set("cursor", cursor)
	return "<" + parameters.path + "?" + query.Encode() + `>; rel="next"`
}
