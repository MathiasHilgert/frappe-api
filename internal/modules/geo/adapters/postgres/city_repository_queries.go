package postgres

// cityColumns selects a city with its name in the locale $1, falling back
// to the place's own name.
const cityColumns = `SELECT cities.place_id AS id,
	coalesce(localized.name, places.name) AS name,
	cities.country_code, cities.subdivision_id, cities.time_zone_id, cities.population,
	cities.latitude::float8 AS latitude, cities.longitude::float8 AS longitude
FROM cities
JOIN places ON places.id = cities.place_id
LEFT JOIN place_names localized ON localized.place_id = places.id AND localized.locale = $1`

// listCitiesQuery is one page of cities after the id $2, at most $5, in
// id order, only of the country $3 and the subdivision $4 when those are
// not null.
const listCitiesQuery = "-- name: geo.list_cities\n" + cityColumns + `
WHERE cities.place_id > $2
	AND ($3::text IS NULL OR cities.country_code = $3)
	AND ($4::bigint IS NULL OR cities.subdivision_id = $4)
ORDER BY cities.place_id
LIMIT $5`

// citiesByIDQuery is the cities with the ids $2.
const citiesByIDQuery = "-- name: geo.cities_by_id\n" + cityColumns + `
WHERE cities.place_id = ANY($2)`
