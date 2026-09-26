package postgres

import "github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"

// timeZoneRow is one row of timeZoneColumns.
type timeZoneRow struct {
	CountryCode *string `db:"country_code"`
	ID          string  `db:"id"`
}

func (row timeZoneRow) timeZone() domain.TimeZone {
	return domain.TimeZone{ID: row.ID, CountryCode: row.CountryCode}
}
