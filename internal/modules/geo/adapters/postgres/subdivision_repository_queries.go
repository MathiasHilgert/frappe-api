package postgres

// subdivisionColumns selects a subdivision with its name in the locale
// $1, falling back to the place's own name.
const subdivisionColumns = `SELECT subdivisions.place_id AS id,
	coalesce(localized.name, places.name) AS name,
	subdivisions.iso_code, subdivisions.country_code
FROM subdivisions
JOIN places ON places.id = subdivisions.place_id
LEFT JOIN place_names localized ON localized.place_id = places.id AND localized.locale = $1`

// listSubdivisionsQuery is one page of subdivisions after the id $2, at
// most $5, in id order, only of the country $3 and with the ISO 3166-2
// code $4 when those are not null.
const listSubdivisionsQuery = "-- name: geo.list_subdivisions\n" + subdivisionColumns + `
WHERE subdivisions.place_id > $2
	AND ($3::text IS NULL OR subdivisions.country_code = $3)
	AND ($4::text IS NULL OR subdivisions.iso_code = $4)
ORDER BY subdivisions.place_id
LIMIT $5`

// subdivisionsByIDQuery is the subdivisions with the ids $2.
const subdivisionsByIDQuery = "-- name: geo.subdivisions_by_id\n" + subdivisionColumns + `
WHERE subdivisions.place_id = ANY($2)`
