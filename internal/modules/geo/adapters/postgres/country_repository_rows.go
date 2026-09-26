package postgres

import "github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"

// countryRow is one row of countryColumns.
type countryRow struct {
	CurrencyCode      *string `db:"currency_code"`
	CapitalCityID     *int64  `db:"capital_city_id"`
	DefaultTimeZoneID *string `db:"default_time_zone_id"`
	Code              string  `db:"code"`
	Name              string  `db:"name"`
	Alpha3Code        string  `db:"alpha3_code"`
	ContinentCode     string  `db:"continent_code"`
	PlaceID           int64   `db:"place_id"`
	NumericCode       int     `db:"numeric_code"`
}

func (row countryRow) country() domain.Country {
	return domain.Country{
		Code:              row.Code,
		PlaceID:           row.PlaceID,
		Name:              row.Name,
		Alpha3Code:        row.Alpha3Code,
		NumericCode:       row.NumericCode,
		ContinentCode:     row.ContinentCode,
		CurrencyCode:      row.CurrencyCode,
		CapitalCityID:     row.CapitalCityID,
		DefaultTimeZoneID: row.DefaultTimeZoneID,
	}
}
