package postgres

// countryColumns selects a country with its name in the locale $1,
// falling back to the place's own name.
const countryColumns = `SELECT countries.code, countries.place_id,
	coalesce(localized.name, places.name) AS name,
	countries.alpha3_code, countries.numeric_code, countries.continent_code, countries.currency_code,
	countries.capital_city_id, countries.default_time_zone_id
FROM countries
JOIN places ON places.id = countries.place_id
LEFT JOIN place_names localized ON localized.place_id = places.id AND localized.locale = $1`

// listCountriesQuery is one page of countries after the code $2, at most
// $3, in code order.
const listCountriesQuery = "-- name: geo.list_countries\n" + countryColumns + `
WHERE countries.code > $2
ORDER BY countries.code
LIMIT $3`

// countriesByCodeQuery is the countries with the codes $2.
const countriesByCodeQuery = "-- name: geo.countries_by_code\n" + countryColumns + `
WHERE countries.code = ANY($2)`
