package postgres

import "github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"

// subdivisionRow is one row of subdivisionColumns.
type subdivisionRow struct {
	ISOCode     *string `db:"iso_code"`
	Name        string  `db:"name"`
	CountryCode string  `db:"country_code"`
	ID          int64   `db:"id"`
}

func (row subdivisionRow) subdivision() domain.Subdivision {
	return domain.Subdivision{ID: row.ID, Name: row.Name, ISOCode: row.ISOCode, CountryCode: row.CountryCode}
}
