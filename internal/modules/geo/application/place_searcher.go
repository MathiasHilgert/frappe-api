package application

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SearchPosition is a search result's place in the ranking: by Score
// (higher first), then KindRank (countries 0, subdivisions 1, cities 2:
// lower first), then Population (higher first), then PlaceID. It is also
// the keyset position of the next page.
type SearchPosition struct {
	Score      float64 `json:"score"`
	KindRank   int     `json:"kind_rank"`
	Population int64   `json:"population"`
	PlaceID    int64   `json:"place_id"`
}

// SearchQuery is one fuzzy search over place names.
type SearchQuery struct {
	// Locale selects which localized names are searched besides every
	// place's own name.
	Locale i18n.Locale
	// After is the last raw match of the previous page, nil for the first.
	After *SearchPosition
	// SubdivisionID keeps only cities of that subdivision, when set.
	SubdivisionID *int64
	// Text is what the user typed; accents and case are ignored.
	Text string
	// CountryCode keeps only places of that country, when set.
	CountryCode string
	// Kinds keeps only places of these kinds; empty means every kind.
	Kinds []domain.PlaceKind
	// Limit is the most matches returned.
	Limit int
}

// Match is one ranked search hit, before its place is loaded.
type Match struct {
	// Kind is the matched place's kind.
	Kind domain.PlaceKind
	// CountryCode is the matched country's code (Kind country only).
	CountryCode string
	// Position is the hit's ranking position.
	Position SearchPosition
	// PlaceID is the matched place's GeoNames id.
	PlaceID int64
}

// PlaceSearcher finds places by name, best match first.
type PlaceSearcher interface {
	Search(ctx context.Context, query SearchQuery) ([]Match, error)
}
