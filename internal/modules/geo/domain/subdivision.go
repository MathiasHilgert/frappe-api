package domain

// Subdivision is a first-level administrative division of a country.
//
//nolint:govet // fieldalignment: fields follow the resource's reading order.
type Subdivision struct {
	// ID is the GeoNames id, the subdivision's public id.
	ID int64
	// Name is the name in the requested locale, else the place's own
	// name in the source data set.
	Name string
	// ISOCode is the ISO 3166-2 code, or nil for units without one.
	ISOCode *string
	// CountryCode is the ISO 3166-1 alpha-2 code of its country.
	CountryCode string
}
