package postgres

import "github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"

// cityRow is one row of cityColumns.
type cityRow struct {
	SubdivisionID *int64  `db:"subdivision_id"`
	Name          string  `db:"name"`
	CountryCode   string  `db:"country_code"`
	TimeZoneID    string  `db:"time_zone_id"`
	ID            int64   `db:"id"`
	Population    int64   `db:"population"`
	Latitude      float64 `db:"latitude"`
	Longitude     float64 `db:"longitude"`
}

func (row cityRow) city() domain.City {
	return domain.City{
		ID:            row.ID,
		Name:          row.Name,
		CountryCode:   row.CountryCode,
		SubdivisionID: row.SubdivisionID,
		TimeZoneID:    row.TimeZoneID,
		Population:    row.Population,
		Latitude:      row.Latitude,
		Longitude:     row.Longitude,
	}
}
