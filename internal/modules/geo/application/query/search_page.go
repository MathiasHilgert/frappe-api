package query

import (
	"errors"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// MinimumQueryLength is the fewest characters (after trimming spaces) a
// search query must have: shorter ones match almost everything.
const MinimumQueryLength = 2

// ErrQueryTooShort reports a search query shorter than
// MinimumQueryLength.
var ErrQueryTooShort = errors.New("geo: search query too short")

// SearchReaders are the ports every search reads through: the searcher
// ranks matches, the readers load the matched places.
type SearchReaders struct {
	Places       application.PlaceSearcher
	Countries    application.CountryReader
	Subdivisions application.SubdivisionReader
	Cities       application.CityReader
}

// SearchResult is one loaded search hit.
type SearchResult struct {
	Place    domain.Place
	Position application.SearchPosition
}

// SearchPage is one page of search results.
type SearchPage struct {
	// Next is the position the next page starts after, nil on the last
	// page. It is the last raw match of this page, so a match whose
	// place could not be loaded never ends pagination early.
	Next *application.SearchPosition
	// Results are the page's loaded places, in ranking order.
	Results []SearchResult
}
