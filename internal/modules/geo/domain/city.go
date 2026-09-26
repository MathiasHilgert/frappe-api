package domain

// City is a populated place.
//
//nolint:govet // fieldalignment: fields follow the resource's reading order.
type City struct {
	// ID is the GeoNames id, the city's public id.
	ID int64
	// Name is the name in the requested locale, else the place's own
	// name in the source data set.
	Name string
	// CountryCode is the ISO 3166-1 alpha-2 code of its country.
	CountryCode string
	// SubdivisionID is its subdivision's GeoNames id, or nil when
	// GeoNames assigns it to none.
	SubdivisionID *int64
	// TimeZoneID is the IANA id of its time zone.
	TimeZoneID string
	// Population is the number of inhabitants.
	Population int64
	// Latitude and Longitude are WGS 84 degrees.
	Latitude  float64
	Longitude float64
}
