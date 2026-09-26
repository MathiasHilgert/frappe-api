package postgres

import (
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// matchRow is one row of searchPlacesQuery.
type matchRow struct {
	Kind        string  `db:"kind"`
	CountryCode string  `db:"country_code"`
	Score       float64 `db:"score"`
	ID          int64   `db:"id"`
	Population  int64   `db:"population"`
	KindRank    int32   `db:"kind_rank"`
}

func (row matchRow) match() application.Match {
	return application.Match{
		Kind:        domain.PlaceKind(row.Kind),
		CountryCode: row.CountryCode,
		PlaceID:     row.ID,
		Position: application.SearchPosition{
			Score: row.Score, KindRank: int(row.KindRank), Population: row.Population, PlaceID: row.ID,
		},
	}
}
