package domain

// Place is one search result: exactly one of Country, Subdivision and
// City is set, matching Kind.
type Place struct {
	Country     *Country
	Subdivision *Subdivision
	City        *City
	Kind        PlaceKind
}
