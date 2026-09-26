package http

import (
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// Country is the country resource.
type Country struct {
	Object      string                        `json:"object" enum:"country" doc:"Always \"country\"." example:"country"`
	ID          string                        `json:"id" doc:"ISO 3166-1 alpha-2 code." example:"AR"`
	Name        string                        `json:"name" doc:"Name in the response language (Content-Language), else the country's own name." example:"Argentina"`
	Alpha3Code  string                        `json:"alpha3_code" doc:"ISO 3166-1 alpha-3 code." example:"ARG"`
	NumericCode string                        `json:"numeric_code" doc:"ISO 3166-1 numeric code, three digits." example:"032"`
	Continent   string                        `json:"continent" enum:"AF,AN,AS,EU,NA,OC,SA" doc:"Continent code." example:"SA"`
	Currency    *string                       `json:"currency" nullable:"true" doc:"ISO 4217 currency code, or null." example:"ARS"`
	CapitalCity rest.NullableExpandable[City] `json:"capital_city" doc:"Capital city id, the city when expanded, or null when unknown."`
}

// from is the resource of country, relations unexpanded.
func (Country) from(country domain.Country) Country {
	resource := Country{
		Object:      "country",
		ID:          country.Code,
		Name:        country.Name,
		Alpha3Code:  country.Alpha3Code,
		NumericCode: fmt.Sprintf("%03d", country.NumericCode),
		Continent:   country.ContinentCode,
		Currency:    country.CurrencyCode,
	}
	if country.CapitalCityID != nil {
		resource.CapitalCity = rest.NullableExpandableID[City](handler{}.placeIDText(*country.CapitalCityID))
	}
	return resource
}

// Expansion paths of a country.
const expandCapitalCity = "capital_city"

// countryExpansions is the expand[] allowlist of every country operation.
var countryExpansions = rest.NewExpansions(expandCapitalCity)

// ListCountriesInput is the input of GET /geo/countries.
type ListCountriesInput struct {
	rest.ExpandParameters
	rest.PageParameters
}

// GetCountryInput is the input of GET /geo/countries/{id}.
type GetCountryInput struct {
	ID string `path:"id" doc:"ISO 3166-1 alpha-2 code." example:"AR"`
	rest.ExpandParameters
}

// CountryOutput is a single country.
type CountryOutput struct {
	Body Country
}
