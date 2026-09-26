package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// PlaceSearchRepository is the Postgres application.PlaceSearcher.
type PlaceSearchRepository struct {
	connection connection
}

var _ application.PlaceSearcher = (*PlaceSearchRepository)(nil)

// NewPlaceSearchRepository returns a PlaceSearchRepository reading
// through pool.
func NewPlaceSearchRepository(pool PoolSource) *PlaceSearchRepository {
	return &PlaceSearchRepository{connection: connection{source: pool}}
}

// allKinds is the kind filter of a search over every kind.
var allKinds = []string{string(domain.KindCountry), string(domain.KindSubdivision), string(domain.KindCity)}

// Search returns the best matches of search in ranking order, after
// search.After when set.
func (repository *PlaceSearchRepository) Search(ctx context.Context, search application.SearchQuery) ([]application.Match, error) {
	kinds := allKinds
	if len(search.Kinds) > 0 {
		kinds = make([]string, 0, len(search.Kinds))
		for _, kind := range search.Kinds {
			kinds = append(kinds, string(kind))
		}
	}
	var afterScore *float64
	var after application.SearchPosition
	if search.After != nil {
		after = *search.After
		afterScore = &after.Score
	}
	statements := repository.connection
	rows, err := statements.query(ctx, searchPlacesQuery,
		search.Text, statements.locale(search.Locale), kinds, statements.optional(search.CountryCode), search.SubdivisionID,
		searchCandidateLimit, afterScore, after.KindRank, after.Population, after.PlaceID, search.Limit)
	if err != nil {
		return nil, err
	}
	matchRows, err := pgx.CollectRows(rows, pgx.RowToStructByName[matchRow])
	if err != nil {
		return nil, fmt.Errorf("geo search: %w", err)
	}
	matches := make([]application.Match, 0, len(matchRows))
	for _, row := range matchRows {
		matches = append(matches, row.match())
	}
	return matches, nil
}
