-- +goose Up
-- +goose StatementBegin
-- Geographic reference data for the geo module: countries, their
-- first-level subdivisions (provinces, states), cities, IANA time zones and
-- localized names, seeded from a GeoNames snapshot (CC BY 4.0; see NOTICE)
-- by the Go migration registered in migrations/geo.go. It is global
-- reference data: no tenant, no Row Level Security, read-only for the
-- application role.
--
-- pg_trgm and unaccent are trusted extensions, so the migration role,
-- which owns the database, creates them without superuser rights.
CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
-- +goose StatementEnd

-- +goose StatementBegin
CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;
-- +goose StatementEnd

-- +goose StatementBegin
-- geo_search_key is the normalized form every geographic name is indexed
-- and searched by: lower case, accents removed ("Córdoba" -> "cordoba").
-- unaccent() is only STABLE (its dictionary could change), so it cannot be
-- used in an index expression directly; this wrapper pins the dictionary
-- and schema and is declared IMMUTABLE, the standard, documented approach.
-- A search must use the same expression, for example
-- geo_search_key(name) % geo_search_key($1), to hit the indexes below.
CREATE FUNCTION geo_search_key(value text) RETURNS text
    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
    RETURN lower(public.unaccent('public.unaccent'::regdictionary, value));
-- +goose StatementEnd

-- +goose StatementBegin
-- countries: ISO 3166-1 countries and territories. code is alpha-2.
-- name is the GeoNames English short name, the fallback for every locale
-- without a country_names row.
CREATE TABLE countries (
    code text PRIMARY KEY CHECK (code ~ '^[A-Z]{2}$'),
    alpha3_code text NOT NULL UNIQUE CHECK (alpha3_code ~ '^[A-Z]{3}$'),
    numeric_code smallint NOT NULL UNIQUE CHECK (numeric_code BETWEEN 0 AND 999),
    geonames_id bigint NOT NULL UNIQUE,
    name text NOT NULL CHECK (name <> ''),
    continent_code text NOT NULL CHECK (continent_code IN ('AF', 'AN', 'AS', 'EU', 'NA', 'OC', 'SA')),
    currency_code text CHECK (currency_code ~ '^[A-Z]{3}$')
);
-- +goose StatementEnd

-- +goose StatementBegin
-- time_zones: IANA time zone identifiers. The offsets (hours from UTC on
-- January 1st and July 1st of the snapshot year, and without daylight
-- saving time) are informational only: compute real offsets with the IANA
-- database at runtime (time.LoadLocation), never from these columns.
CREATE TABLE time_zones (
    id text PRIMARY KEY CHECK (id <> ''),
    country_code text REFERENCES countries (code),
    january_offset_hours numeric(4, 2) NOT NULL,
    july_offset_hours numeric(4, 2) NOT NULL,
    raw_offset_hours numeric(4, 2) NOT NULL
);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX time_zones_country_code ON time_zones (country_code);
-- +goose StatementEnd

-- +goose StatementBegin
-- subdivisions: first-level administrative divisions. id is the GeoNames
-- id; code is the GeoNames admin1 code, unique within a country (mostly
-- FIPS codes, ISO 3166-2 for a few countries; GeoNames does not publish a
-- full ISO 3166-2 mapping). name is the GeoNames ASCII name.
CREATE TABLE subdivisions (
    id bigint PRIMARY KEY,
    country_code text NOT NULL REFERENCES countries (code),
    code text NOT NULL CHECK (code <> ''),
    name text NOT NULL CHECK (name <> ''),
    ascii_name text NOT NULL,
    UNIQUE (country_code, code)
);
-- +goose StatementEnd

-- +goose StatementBegin
-- cities: populated places. id is the GeoNames id. subdivision_id is NULL
-- when GeoNames assigns the city to no known subdivision. feature_code is
-- the GeoNames feature code (PPLC national capital, PPLA subdivision
-- seat, PPL populated place, ...).
CREATE TABLE cities (
    id bigint PRIMARY KEY,
    country_code text NOT NULL REFERENCES countries (code),
    subdivision_id bigint REFERENCES subdivisions (id),
    name text NOT NULL CHECK (name <> ''),
    ascii_name text NOT NULL,
    latitude numeric(7, 5) NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude numeric(8, 5) NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    population bigint NOT NULL CHECK (population >= 0),
    feature_code text NOT NULL CHECK (feature_code <> ''),
    time_zone_id text NOT NULL REFERENCES time_zones (id)
);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX cities_country_code ON cities (country_code);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX cities_subdivision_id ON cities (subdivision_id);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX cities_time_zone_id ON cities (time_zone_id);
-- +goose StatementEnd

-- +goose StatementBegin
-- Localized names: one table per entity, so every name has a real foreign
-- key to its entity and to locales (a single polymorphic table could not).
-- A missing row means "use the entity's own name". Reference data, so
-- these do not use localized_texts (tenant-scoped, user-entered text).
CREATE TABLE country_names (
    country_code text NOT NULL REFERENCES countries (code) ON DELETE CASCADE,
    locale text NOT NULL REFERENCES locales (code),
    name text NOT NULL CHECK (name <> ''),
    PRIMARY KEY (country_code, locale)
);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE TABLE subdivision_names (
    subdivision_id bigint NOT NULL REFERENCES subdivisions (id) ON DELETE CASCADE,
    locale text NOT NULL REFERENCES locales (code),
    name text NOT NULL CHECK (name <> ''),
    PRIMARY KEY (subdivision_id, locale)
);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE TABLE city_names (
    city_id bigint NOT NULL REFERENCES cities (id) ON DELETE CASCADE,
    locale text NOT NULL REFERENCES locales (code),
    name text NOT NULL CHECK (name <> ''),
    PRIMARY KEY (city_id, locale)
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Trigram search indexes over the normalized original and localized names.
CREATE INDEX countries_name_search ON countries USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX subdivisions_name_search ON subdivisions USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX cities_name_search ON cities USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX country_names_name_search ON country_names USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX subdivision_names_name_search ON subdivision_names USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE INDEX city_names_name_search ON city_names USING gin (geo_search_key(name) gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
-- The application only reads reference data; it changes through
-- migrations. Default privileges granted write access, so revoke first.
REVOKE ALL ON countries, time_zones, subdivisions, cities, country_names, subdivision_names, city_names
    FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT ON countries, time_zones, subdivisions, cities, country_names, subdivision_names, city_names
    TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE city_names, subdivision_names, country_names, cities, subdivisions, time_zones, countries;
-- +goose StatementEnd

-- +goose StatementBegin
DROP FUNCTION geo_search_key(text);
-- +goose StatementEnd

-- +goose StatementBegin
DROP EXTENSION IF EXISTS unaccent;
-- +goose StatementEnd

-- +goose StatementBegin
DROP EXTENSION IF EXISTS pg_trgm;
-- +goose StatementEnd
