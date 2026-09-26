package domain

// PlaceKind is the kind of a place: every country, subdivision and city
// is a place with a stable GeoNames id.
type PlaceKind string

// Place kinds.
const (
	KindCountry     PlaceKind = "country"
	KindSubdivision PlaceKind = "subdivision"
	KindCity        PlaceKind = "city"
)
