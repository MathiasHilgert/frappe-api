package rest

// ListObject is the "object" value of every list envelope.
const ListObject = "list"

// List is the envelope of every collection response (Stripe style):
//
//	{"object":"list","url":"/v1/geo/cities","data":[...],"has_more":true,"next_cursor":"..."}
//
// data is always an array, never null. next_cursor is null on the last
// page; pass it back as ?cursor= to get the next one. Build it with
// NewList, or with NewPage in a handler.
//
//nolint:govet // fieldalignment: field order is the JSON property order, "object" first like every resource.
type List[T any] struct {
	// Object is always "list".
	Object string `json:"object" enum:"list" doc:"Always \"list\"."`
	// URL is the path of the collection, without query parameters.
	URL string `json:"url" doc:"Path of the collection this list belongs to." example:"/v1/geo/cities"`
	// Data holds this page's resources, in the collection's order.
	Data []T `json:"data" doc:"Resources on this page."`
	// HasMore reports whether a next page exists.
	HasMore bool `json:"has_more" doc:"Whether another page exists after this one."`
	// NextCursor is the opaque cursor of the next page, or nil on the
	// last page.
	NextCursor *string `json:"next_cursor" nullable:"true" doc:"Opaque cursor for the next page; null on the last page."`
}

// NewList returns the envelope for data at url. nextCursor is the opaque
// cursor of the next page, or "" when this is the last page.
func NewList[T any](url string, data []T, nextCursor string) List[T] {
	if data == nil {
		data = []T{}
	}
	list := List[T]{Object: ListObject, URL: url, Data: data}
	if nextCursor != "" {
		list.HasMore = true
		list.NextCursor = &nextCursor
	}
	return list
}

// ListOutput is the Huma output of a paginated collection operation: the
// envelope as body, and an RFC 8288 Link header with rel="next" when a
// next page exists (GitHub style), so generic HTTP clients can follow
// pages without parsing the body.
type ListOutput[T any] struct {
	// Body is the list envelope.
	Body List[T]
	// Link is `<next page URL>; rel="next"`, or empty on the last page.
	Link string `header:"Link" doc:"RFC 8288 link to the next page, rel=\"next\"; absent on the last page."`
}
