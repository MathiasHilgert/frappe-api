package postgres

// searchCandidateLimit bounds the hits each name branch of a search
// contributes before ranking, so a common query ("san") never ranks every
// matching place of the snapshot. Deep pages stop there: a search returns
// at most this many places per branch, which is far beyond what an
// autocomplete shows. Each branch orders its hits by score, then id, so
// the candidates, and so every page, are deterministic.
const searchCandidateLimit = 1000

// hitFilter keeps the hits of the kinds $3, of the country $4 and of the
// subdivision $5 (cities only) when those are not null. It is repeated
// in both hit branches, so the filters apply before the pre-limit.
const hitFilter = `
	LEFT JOIN countries ON countries.place_id = places.id
	LEFT JOIN subdivisions ON subdivisions.place_id = places.id
	LEFT JOIN cities ON cities.place_id = places.id
	WHERE places.kind = ANY($3)
		AND ($4::text IS NULL OR coalesce(countries.code, subdivisions.country_code, cities.country_code) = $4)
		AND ($5::bigint IS NULL OR cities.subdivision_id = $5)
	ORDER BY score DESC, places.id
	LIMIT $6`

// searchPlacesQuery finds the places whose own name ($2 null) or name in
// the locale $2 contains a word similar to $1 ("key <% search_key",
// pg_trgm word similarity, default threshold 0.6, served by the GIN
// trigram indexes). A hit's score is the mean of word similarity (the
// query is a close prefix or word of the name) and whole similarity (the
// name is close to the query as a whole), so "Cordoba" ranks above "Villa
// Cordoba" for "cordoba"; a place keeps its best name's score.
//
// Ties rank by kind (countries, subdivisions, cities), then population
// (cities', or the sum of their cities' for subdivisions and countries),
// then id. After the position ($7 score, $8 kind rank, $9 population, $10
// id; $7 null for the first page), at most $11.
const searchPlacesQuery = `-- name: geo.search_places
WITH search AS (SELECT geo_search_key($1) AS key),
own_hits AS (
	SELECT places.id AS place_id,
		(word_similarity(search.key, places.search_key) + similarity(search.key, places.search_key))::float8 / 2 AS score
	FROM search
	JOIN places ON search.key <% places.search_key` + hitFilter + `
),
localized_hits AS (
	SELECT places.id AS place_id,
		(word_similarity(search.key, place_names.search_key) + similarity(search.key, place_names.search_key))::float8 / 2 AS score
	FROM search
	JOIN place_names ON place_names.locale = $2 AND search.key <% place_names.search_key
	JOIN places ON places.id = place_names.place_id` + hitFilter + `
),
ranked AS (
	SELECT hits.place_id, max(hits.score) AS score
	FROM (SELECT * FROM own_hits UNION ALL SELECT * FROM localized_hits) hits
	GROUP BY hits.place_id
),
positioned AS (
	SELECT places.id, places.kind, coalesce(countries.code, '') AS country_code, ranked.score,
		CASE places.kind WHEN 'country' THEN 0 WHEN 'subdivision' THEN 1 ELSE 2 END AS kind_rank,
		coalesce(countries.population, subdivisions.population, cities.population, 0) AS population
	FROM ranked
	JOIN places ON places.id = ranked.place_id
	LEFT JOIN countries ON countries.place_id = places.id
	LEFT JOIN subdivisions ON subdivisions.place_id = places.id
	LEFT JOIN cities ON cities.place_id = places.id
)
SELECT id, kind, country_code, score, kind_rank, population
FROM positioned
WHERE $7::float8 IS NULL OR score < $7 OR (score = $7 AND (kind_rank > $8
	OR (kind_rank = $8 AND (population < $9 OR (population = $9 AND id > $10)))))
ORDER BY score DESC, kind_rank, population DESC, id
LIMIT $11`
