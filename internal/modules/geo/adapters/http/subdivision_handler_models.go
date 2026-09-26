package http

import (
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// Subdivision is the first-level subdivision resource.
//
//nolint:govet // fieldalignment: field order is the JSON property order, "object" first.
type Subdivision struct {
	Object  string                   `json:"object" enum:"subdivision" doc:"Always \"subdivision\"." example:"subdivision"`
	ID      string                   `json:"id" doc:"GeoNames id." example:"3860255"`
	Name    string                   `json:"name" doc:"Name in the response language (Content-Language), else the subdivision's own name." example:"Cordoba"`
	ISOCode *string                  `json:"iso_code" nullable:"true" doc:"ISO 3166-2 code, or null for units without one." example:"AR-X"`
	Country rest.Expandable[Country] `json:"country" doc:"Country id, or the country when expanded."`
}

// from is the resource of subdivision, relations unexpanded.
func (Subdivision) from(subdivision domain.Subdivision) Subdivision {
	return Subdivision{
		Object:  "subdivision",
		ID:      handler{}.placeIDText(subdivision.ID),
		Name:    subdivision.Name,
		ISOCode: subdivision.ISOCode,
		Country: rest.ExpandableID[Country](subdivision.CountryCode),
	}
}

// subdivisionExpansions is the expand[] allowlist of every subdivision
// operation.
var subdivisionExpansions = rest.NewExpansions(expandCountry)

// ListSubdivisionsInput is the input of GET /geo/subdivisions.
type ListSubdivisionsInput struct {
	Country string `query:"country" pattern:"^[A-Z]{2}$" doc:"Only subdivisions of this country (ISO 3166-1 alpha-2)." example:"AR"`
	ISOCode string `query:"iso_code" pattern:"^[A-Z]{2}-[A-Z0-9]{1,3}$" doc:"Only the subdivision with this ISO 3166-2 code." example:"AR-X"`
	rest.ExpandParameters
	rest.PageParameters
}

// GetSubdivisionInput is the input of GET /geo/subdivisions/{id}.
type GetSubdivisionInput struct {
	ID string `path:"id" doc:"GeoNames id." example:"3860255"`
	rest.ExpandParameters
}

// SubdivisionOutput is a single subdivision.
type SubdivisionOutput struct {
	Body Subdivision
}
