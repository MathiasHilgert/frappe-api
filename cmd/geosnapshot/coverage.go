package main

// latinAmericanCountries lists, by ISO 3166-1 alpha-2 code, every country
// and territory of South America, Central America (including Mexico) and
// the Caribbean. Their cities are kept down to latinAmericanMinimumPopulation;
// the rest of the world only down to worldMinimumPopulation.
var latinAmericanCountries = map[string]bool{ //nolint:gochecknoglobals // constant lookup table.
	// South America.
	"AR": true, "BO": true, "BR": true, "CL": true, "CO": true, "EC": true,
	"FK": true, "GF": true, "GY": true, "PE": true, "PY": true, "SR": true,
	"UY": true, "VE": true,
	// Central America and Mexico.
	"BZ": true, "CR": true, "GT": true, "HN": true, "MX": true, "NI": true,
	"PA": true, "SV": true,
	// Caribbean.
	"AG": true, "AI": true, "AW": true, "BB": true, "BL": true, "BQ": true,
	"BS": true, "CU": true, "CW": true, "DM": true, "DO": true, "GD": true,
	"GP": true, "HT": true, "JM": true, "KN": true, "KY": true, "LC": true,
	"MF": true, "MQ": true, "MS": true, "PR": true, "SX": true, "TC": true,
	"TT": true, "VC": true, "VG": true, "VI": true,
}

const (
	// latinAmericanMinimumPopulation: Latin American cities are kept when
	// their population is strictly greater than this.
	latinAmericanMinimumPopulation = 500
	// worldMinimumPopulation: cities elsewhere are kept when their
	// population is strictly greater than this.
	worldMinimumPopulation = 15000
	// capitalFeatureCode marks a national capital, kept whatever its
	// population so every country has its capital.
	capitalFeatureCode = "PPLC"
)

// excludedFeatureCodes are populated place kinds that are not a city a
// person lives in today: sections of a larger place (neighborhoods),
// historical, abandoned and destroyed places. See
// https://www.geonames.org/export/codes.html.
var excludedFeatureCodes = map[string]bool{ //nolint:gochecknoglobals // constant lookup table.
	"PPLX":  true,
	"PPLH":  true,
	"PPLQ":  true,
	"PPLW":  true,
	"PPLCH": true,
}

// includeCity reports whether the snapshot covers candidate.
func includeCity(candidate city) bool {
	if excludedFeatureCodes[candidate.featureCode] {
		return false
	}
	if candidate.featureCode == capitalFeatureCode {
		return true
	}
	if latinAmericanCountries[candidate.countryCode] {
		return candidate.population > latinAmericanMinimumPopulation
	}
	return candidate.population > worldMinimumPopulation
}
