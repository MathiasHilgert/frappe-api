package http

import (
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// TimeZone is the IANA time zone resource.
//
//nolint:govet // fieldalignment: field order is the JSON property order, "object" first.
type TimeZone struct {
	Object  string  `json:"object" enum:"time_zone" doc:"Always \"time_zone\"." example:"time_zone"`
	ID      string  `json:"id" doc:"IANA time zone id." example:"America/Argentina/Cordoba"`
	Country *string `json:"country" nullable:"true" doc:"ISO 3166-1 alpha-2 code of its country, or null." example:"AR"`
}

// from is the resource of timeZone.
func (TimeZone) from(timeZone domain.TimeZone) TimeZone {
	return TimeZone{Object: "time_zone", ID: timeZone.ID, Country: timeZone.CountryCode}
}

// ListTimeZonesInput is the input of GET /geo/time_zones.
type ListTimeZonesInput struct {
	rest.PageParameters
}

// GetTimeZoneInput is the input of GET /geo/time_zones/{id...}.
type GetTimeZoneInput struct {
	ID string `path:"id" doc:"IANA time zone id; its slashes stay unescaped in the path." example:"America/Argentina/Cordoba"`
}

// TimeZoneOutput is a single time zone.
type TimeZoneOutput struct {
	Body TimeZone
}
