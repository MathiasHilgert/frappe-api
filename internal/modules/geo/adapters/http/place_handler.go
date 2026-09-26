package http

import (
	"context"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
)

// PlaceQueries are the use cases the place operations run.
type PlaceQueries struct {
	Search usecase.QueryHandler[query.SearchPlaces, query.SearchPage]
}

// PlaceHandler serves /geo/places: the search over every kind of place.
type PlaceHandler struct {
	queries PlaceQueries
	handler
}

// NewPlaceHandler returns a PlaceHandler running queries.
func NewPlaceHandler(shared Shared, queries PlaceQueries) *PlaceHandler {
	return &PlaceHandler{handler: handler{shared: shared}, queries: queries}
}

// Register adds the place operations onto api.
func (places *PlaceHandler) Register(api huma.API) {
	huma.Register(api, places.operation("search-places", "/geo/places/search", "Search places",
		"Autocomplete over countries, subdivisions and cities at once. Each item is a country, subdivision or city resource, "+
			"told apart by its object. Ranked by name similarity (own name or name in the response language), then kind "+
			"(countries, subdivisions, cities), then population."),
		places.search)
}

func (places *PlaceHandler) search(ctx context.Context, input *SearchPlacesInput) (*rest.ListOutput[Place], error) {
	after, err := places.searchAfter(ctx, input.PageParameters)
	if err != nil {
		return nil, err
	}
	locale := places.locale(ctx)
	if err = places.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	page, err := places.queries.Search.Handle(ctx, query.SearchPlaces{
		Locale: locale, After: after, Text: input.Query, CountryCode: input.Country, Limit: input.Limit,
	})
	if err != nil {
		return nil, places.failure(ctx, err)
	}
	resources := make([]Place, 0, len(page.Results))
	for _, result := range page.Results {
		resources = append(resources, Place{}.from(result.Place))
	}
	return rest.NewCursorPage(places.shared.Cursors, input.PageParameters, resources, places.next(page))
}
