package http

import (
	"encoding/json"
	"reflect"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SearchParameters are the query parameters of every search operation.
type SearchParameters struct {
	Query string `query:"query" required:"true" minLength:"2" maxLength:"100" doc:"Text to search for; case and accents are ignored and small typos tolerated. At least 2 characters besides spaces." example:"cordoba"`
}

// Place is one unified search result: a country, a subdivision or a
// city, told apart by its "object".
type Place struct {
	resource any
}

// from is the resource of place.
func (Place) from(place domain.Place) Place {
	switch {
	case place.Country != nil:
		return Place{resource: Country{}.from(*place.Country)}
	case place.Subdivision != nil:
		return Place{resource: Subdivision{}.from(*place.Subdivision)}
	default:
		return Place{resource: City{}.from(*place.City)}
	}
}

// MarshalJSON writes the wrapped resource.
func (place Place) MarshalJSON() ([]byte, error) {
	return json.Marshal(place.resource)
}

// Schema declares a place as oneOf the three resources, discriminated by
// their "object" property, so generated clients pick the right type.
func (Place) Schema(registry huma.Registry) *huma.Schema {
	country := registry.Schema(reflect.TypeFor[Country](), true, "")
	subdivision := registry.Schema(reflect.TypeFor[Subdivision](), true, "")
	city := registry.Schema(reflect.TypeFor[City](), true, "")
	return &huma.Schema{
		OneOf: []*huma.Schema{country, subdivision, city},
		Discriminator: &huma.Discriminator{
			PropertyName: "object",
			Mapping: map[string]string{
				string(domain.KindCountry):     country.Ref,
				string(domain.KindSubdivision): subdivision.Ref,
				string(domain.KindCity):        city.Ref,
			},
		},
	}
}

// SearchPlacesInput is the input of GET /geo/places/search.
type SearchPlacesInput struct {
	Country string `query:"country" pattern:"^[A-Z]{2}$" doc:"Only places of this country (ISO 3166-1 alpha-2): the country itself, its subdivisions and its cities." example:"AR"`
	SearchParameters
	rest.PageParameters
}
