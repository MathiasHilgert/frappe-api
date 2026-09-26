package http

import (
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// City is the city resource.
type City struct {
	Object      string                               `json:"object" enum:"city" doc:"Always \"city\"." example:"city"`
	ID          string                               `json:"id" doc:"GeoNames id." example:"3860259"`
	Name        string                               `json:"name" doc:"Name in the response language (Content-Language), else the city's own name." example:"Cordoba"`
	Country     rest.Expandable[Country]             `json:"country" doc:"Country id, or the country when expanded."`
	Subdivision rest.NullableExpandable[Subdivision] `json:"subdivision" doc:"Subdivision id, the subdivision when expanded, or null when unknown."`
	TimeZone    string                               `json:"time_zone" doc:"IANA time zone id." example:"America/Argentina/Cordoba"`
	Population  int64                                `json:"population" doc:"Inhabitants, per GeoNames." example:"1428214"`
	Latitude    float64                              `json:"latitude" doc:"WGS 84 latitude in degrees." example:"-31.4135"`
	Longitude   float64                              `json:"longitude" doc:"WGS 84 longitude in degrees." example:"-64.18105"`
}

// from is the resource of city, relations unexpanded.
func (City) from(city domain.City) City {
	resource := City{
		Object:     "city",
		ID:         handler{}.placeIDText(city.ID),
		Name:       city.Name,
		Country:    rest.ExpandableID[Country](city.CountryCode),
		TimeZone:   city.TimeZoneID,
		Population: city.Population,
		Latitude:   city.Latitude,
		Longitude:  city.Longitude,
	}
	if city.SubdivisionID != nil {
		resource.Subdivision = rest.NullableExpandableID[Subdivision](handler{}.placeIDText(*city.SubdivisionID))
	}
	return resource
}

// Expansion paths of a city.
const (
	expandCountry     = "country"
	expandSubdivision = "subdivision"
)

// cityExpansions is the expand[] allowlist of every city operation.
var cityExpansions = rest.NewExpansions(expandCountry, expandSubdivision)

// CityFilters are the filters of listing (and searching) cities.
type CityFilters struct {
	Country     string `query:"country" pattern:"^[A-Z]{2}$" doc:"Only cities of this country (ISO 3166-1 alpha-2)." example:"AR"`
	Subdivision string `query:"subdivision" pattern:"^[1-9][0-9]{0,17}$" doc:"Only cities of this subdivision (GeoNames id)." example:"3860255"`
}

// subdivisionID is the subdivision filter as an id, nil when absent. The
// schema pattern already guarantees a canonical id of at most 18 digits,
// which always fits an int64.
func (filters CityFilters) subdivisionID() *int64 {
	if filters.Subdivision == "" {
		return nil
	}
	id, _ := handler{}.placeID(filters.Subdivision)
	return &id
}

// ListCitiesInput is the input of GET /geo/cities.
type ListCitiesInput struct {
	CityFilters
	rest.ExpandParameters
	rest.PageParameters
}

// GetCityInput is the input of GET /geo/cities/{id}.
type GetCityInput struct {
	ID string `path:"id" doc:"GeoNames id." example:"3860259"`
	rest.ExpandParameters
}

// CityOutput is a single city.
type CityOutput struct {
	Body City
}
