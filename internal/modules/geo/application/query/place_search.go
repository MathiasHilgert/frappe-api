package query

import (
	"context"
	"strings"
	"unicode/utf8"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// placeSearch runs one page of a search for every search handler: it
// validates the text, asks the searcher for one match more than the page
// (which only tells whether a next page exists), loads the matched places
// with one batch per kind and records the business metrics.
type placeSearch struct {
	readers SearchReaders
	// scope names the search in metrics: places, countries, subdivisions
	// or cities.
	scope string
}

func (search placeSearch) run(ctx context.Context, query application.SearchQuery) (SearchPage, error) {
	query.Text = strings.TrimSpace(query.Text)
	if utf8.RuneCountInString(query.Text) < MinimumQueryLength {
		return SearchPage{}, ErrQueryTooShort
	}
	limit := query.Limit
	query.Limit = limit + 1
	matches, err := search.readers.Places.Search(ctx, query)
	if err != nil {
		return SearchPage{}, err
	}
	var page SearchPage
	if len(matches) > limit {
		matches = matches[:limit]
		next := matches[limit-1].Position
		page.Next = &next
	}
	places, err := search.load(ctx, query.Locale, matches)
	if err != nil {
		return SearchPage{}, err
	}
	page.Results = make([]SearchResult, 0, len(matches))
	for _, match := range matches {
		if place, found := places.find(match); found {
			page.Results = append(page.Results, SearchResult{Place: place, Position: match.Position})
		}
	}
	metrics.record(ctx, search.scope, len(page.Results))
	return page, nil
}

// loadedPlaces holds the places of one page of matches, by kind.
type loadedPlaces struct {
	countries    map[string]domain.Country
	subdivisions map[int64]domain.Subdivision
	cities       map[int64]domain.City
}

func (places loadedPlaces) find(match application.Match) (domain.Place, bool) {
	switch match.Kind {
	case domain.KindCountry:
		country, found := places.countries[match.CountryCode]
		return domain.Place{Kind: match.Kind, Country: &country}, found
	case domain.KindSubdivision:
		subdivision, found := places.subdivisions[match.PlaceID]
		return domain.Place{Kind: match.Kind, Subdivision: &subdivision}, found
	case domain.KindCity:
		city, found := places.cities[match.PlaceID]
		return domain.Place{Kind: match.Kind, City: &city}, found
	default:
		return domain.Place{}, false
	}
}

// load loads every matched place with one batch per kind.
func (search placeSearch) load(ctx context.Context, locale i18n.Locale, matches []application.Match) (loadedPlaces, error) {
	var countryCodes []string
	var subdivisionIDs, cityIDs []int64
	for _, match := range matches {
		switch match.Kind {
		case domain.KindCountry:
			countryCodes = append(countryCodes, match.CountryCode)
		case domain.KindSubdivision:
			subdivisionIDs = append(subdivisionIDs, match.PlaceID)
		case domain.KindCity:
			cityIDs = append(cityIDs, match.PlaceID)
		}
	}
	var places loadedPlaces
	var err error
	if places.countries, err = NewFindCountriesHandler(search.readers.Countries).Handle(ctx, FindCountries{Locale: locale, Codes: countryCodes}); err != nil {
		return loadedPlaces{}, err
	}
	if places.subdivisions, err = NewFindSubdivisionsHandler(search.readers.Subdivisions).Handle(ctx, FindSubdivisions{Locale: locale, IDs: subdivisionIDs}); err != nil {
		return loadedPlaces{}, err
	}
	if places.cities, err = NewFindCitiesHandler(search.readers.Cities).Handle(ctx, FindCities{Locale: locale, IDs: cityIDs}); err != nil {
		return loadedPlaces{}, err
	}
	return places, nil
}
