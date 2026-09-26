-- +goose Up
-- +goose StatementBegin
-- Search ranks equally similar places by kind (countries, then
-- subdivisions, then cities) and population. Cities carry GeoNames'
-- population; a subdivision's and a country's is the sum of their cities
-- in the snapshot (cities above 500 or 15000 inhabitants, see README), a
-- ranking signal rather than a census figure. A seed migration that
-- changes cities MUST recompute both.
ALTER TABLE subdivisions ADD COLUMN population bigint NOT NULL DEFAULT 0 CHECK (population >= 0);
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE countries ADD COLUMN population bigint NOT NULL DEFAULT 0 CHECK (population >= 0);
-- +goose StatementEnd

-- +goose StatementBegin
UPDATE subdivisions SET population = totals.population
FROM (SELECT subdivision_id, sum(population) AS population FROM cities WHERE subdivision_id IS NOT NULL GROUP BY subdivision_id) totals
WHERE totals.subdivision_id = subdivisions.place_id;
-- +goose StatementEnd

-- +goose StatementBegin
UPDATE countries SET population = totals.population
FROM (SELECT country_code, sum(population) AS population FROM cities GROUP BY country_code) totals
WHERE totals.country_code = countries.code;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
ALTER TABLE countries DROP COLUMN population;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE subdivisions DROP COLUMN population;
-- +goose StatementEnd
