package domain

// Country is an ISO 3166-1 country or territory.
//
//nolint:govet // fieldalignment: fields follow the resource's reading order.
type Country struct {
	// Code is the ISO 3166-1 alpha-2 code, the country's public id.
	Code string
	// PlaceID is the country's GeoNames id.
	PlaceID int64
	// Name is the name in the requested locale, else the place's own
	// name in the source data set.
	Name string
	// Alpha3Code is the ISO 3166-1 alpha-3 code.
	Alpha3Code string
	// NumericCode is the ISO 3166-1 numeric code.
	NumericCode int
	// ContinentCode is the GeoNames continent code (AF, AN, AS, EU, NA,
	// OC, SA).
	ContinentCode string
	// CurrencyCode is the ISO 4217 code, or nil when there is none.
	CurrencyCode *string
	// CapitalCityID is the capital's GeoNames id, or nil when unknown.
	CapitalCityID *int64
	// DefaultTimeZoneID is the IANA id of the capital's time zone, else
	// the most populous city's, or nil when unknown.
	DefaultTimeZoneID *string
}
