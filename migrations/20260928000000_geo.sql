-- +goose Up
-- +goose StatementBegin
-- Geographic reference data for the geo module: places (countries, their
-- first-level subdivisions and cities), IANA time zones and localized
-- names, seeded by the Go migration in migrations/geo.go from a snapshot
-- built by cmd/geosnapshot (GeoNames, Unicode CLDR and Wikidata; see
-- NOTICE). It is global reference data: no tenant, no Row Level Security,
-- read-only for the application role.
--
-- pg_trgm and unaccent are trusted extensions, so the migration role,
-- which owns the database, creates them without superuser rights. Down
-- keeps them: other schema objects may come to depend on them.
CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
-- +goose StatementEnd

-- +goose StatementBegin
CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;
-- +goose StatementEnd

-- +goose StatementBegin
-- btree_gin (trusted too) lets one GIN index combine an equality column
-- (locale) with the trigram search key, for locale-filtered search.
CREATE EXTENSION IF NOT EXISTS btree_gin WITH SCHEMA public;
-- +goose StatementEnd

-- +goose StatementBegin
-- geo_search_key is the normalized form every place name is indexed and
-- searched by: lower case, accents removed ("Córdoba" -> "cordoba").
-- unaccent() is only STABLE (its dictionary could change), so it cannot be
-- used in an index expression or a generated column directly; this
-- wrapper pins the dictionary and schema and is declared IMMUTABLE, the
-- documented approach. Lower-casing uses the builtin pg_c_utf8 collation
-- (Unicode simple case mapping, part of Postgres itself, independent of
-- the operating system's libc or ICU), so an OS or library upgrade cannot
-- change it. The unaccent rules file and a Postgres major upgrade (new
-- Unicode version) still can: after either, rewrite the stored keys with
-- "UPDATE places SET name = name" and "UPDATE place_names SET name = name"
-- (regenerating search_key) and REINDEX the search indexes. Searches
-- compare against geo_search_key($1).
CREATE FUNCTION geo_search_key(value text) RETURNS text
    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
    RETURN lower(public.unaccent('public.unaccent'::regdictionary, value) COLLATE pg_c_utf8);
-- +goose StatementEnd

-- +goose StatementBegin
-- places is the supertype of countries, subdivisions and cities
-- (class-table inheritance): one row per place, keyed by its GeoNames id.
-- That id is every place's stable public identifier (API routes address
-- countries, subdivisions and cities by it; ISO codes are attributes and
-- filters, since not every subdivision has one). Each row has
-- with its kind, its own name (UTF-8, official: the CLDR English name for
-- countries and subdivisions, the GeoNames name for cities) and the
-- search key of that name. Every subtype row references exactly one place
-- of its own kind through (place_id, kind).
CREATE TABLE places (
    id bigint PRIMARY KEY,
    kind text NOT NULL CHECK (kind IN ('country', 'subdivision', 'city')),
    name text NOT NULL CHECK (name <> ''),
    search_key text NOT NULL GENERATED ALWAYS AS (geo_search_key(name)) STORED,
    UNIQUE (id, kind)
);
-- +goose StatementEnd

-- +goose StatementBegin
-- countries: ISO 3166-1 countries and territories, code is alpha-2.
-- capital_city_id and default_time_zone_id are NULL when unknown (for
-- example Antarctica); their foreign keys are deferred because cities and
-- time zones reference countries too. The default time zone is the
-- capital's, else the most populous city's, else the country's only one.
CREATE TABLE countries (
    code text PRIMARY KEY CHECK (code ~ '^[A-Z]{2}$'),
    place_id bigint NOT NULL UNIQUE,
    kind text NOT NULL DEFAULT 'country' CHECK (kind = 'country'),
    alpha3_code text NOT NULL UNIQUE CHECK (alpha3_code ~ '^[A-Z]{3}$'),
    numeric_code smallint NOT NULL UNIQUE CHECK (numeric_code BETWEEN 0 AND 999),
    continent_code text NOT NULL CHECK (continent_code IN ('AF', 'AN', 'AS', 'EU', 'NA', 'OC', 'SA')),
    currency_code text CHECK (currency_code ~ '^[A-Z]{3}$'),
    capital_city_id bigint,
    default_time_zone_id text,
    FOREIGN KEY (place_id, kind) REFERENCES places (id, kind) ON DELETE CASCADE
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
-- subdivisions: first-level administrative divisions, addressed by
-- place_id (the GeoNames id) like every place. iso_code is the optional
-- ISO 3166-2 code, an attribute and a lookup filter, never the key (from Wikidata, validated against CLDR); it is NULL
-- only for the GeoNames units the reviewed override file of
-- cmd/geosnapshot declares without one (subdivisions of dependent
-- territories, units outside ISO 3166-2). geonames_admin1_code is the
-- GeoNames admin1 code, unique within the country.
CREATE TABLE subdivisions (
    place_id bigint PRIMARY KEY,
    kind text NOT NULL DEFAULT 'subdivision' CHECK (kind = 'subdivision'),
    country_code text NOT NULL REFERENCES countries (code),
    iso_code text UNIQUE CHECK (iso_code ~ '^[A-Z]{2}-[A-Z0-9]{1,3}$' AND left(iso_code, 2) = country_code),
    geonames_admin1_code text NOT NULL CHECK (geonames_admin1_code <> ''),
    UNIQUE (country_code, geonames_admin1_code),
    FOREIGN KEY (place_id, kind) REFERENCES places (id, kind) ON DELETE CASCADE
);
-- +goose StatementEnd

-- +goose StatementBegin
-- cities: populated places. subdivision_id is NULL when GeoNames assigns
-- the city to no known subdivision. ascii_name only helps search.
-- feature_code is the GeoNames feature code (PPLC national capital, PPLA
-- subdivision seat, PPL populated place, ...).
CREATE TABLE cities (
    place_id bigint PRIMARY KEY,
    kind text NOT NULL DEFAULT 'city' CHECK (kind = 'city'),
    country_code text NOT NULL REFERENCES countries (code),
    subdivision_id bigint REFERENCES subdivisions (place_id),
    ascii_name text NOT NULL,
    latitude numeric(7, 5) NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude numeric(8, 5) NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    population bigint NOT NULL CHECK (population >= 0),
    feature_code text NOT NULL CHECK (feature_code <> ''),
    time_zone_id text NOT NULL REFERENCES time_zones (id),
    FOREIGN KEY (place_id, kind) REFERENCES places (id, kind) ON DELETE CASCADE
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
ALTER TABLE countries ADD FOREIGN KEY (capital_city_id) REFERENCES cities (place_id)
    DEFERRABLE INITIALLY DEFERRED;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE countries ADD FOREIGN KEY (default_time_zone_id) REFERENCES time_zones (id)
    DEFERRABLE INITIALLY DEFERRED;
-- +goose StatementEnd

-- +goose StatementBegin
-- place_names: a place's name in a platform locale (CLDR for countries
-- and subdivisions, GeoNames alternate names for cities). A missing row
-- means "use places.name". Reference data, so it does not use
-- localized_texts (tenant-scoped, user-entered text).
CREATE TABLE place_names (
    place_id bigint NOT NULL REFERENCES places (id) ON DELETE CASCADE,
    locale text NOT NULL REFERENCES locales (code),
    name text NOT NULL CHECK (name <> ''),
    search_key text NOT NULL GENERATED ALWAYS AS (geo_search_key(name)) STORED,
    PRIMARY KEY (place_id, locale)
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Unified search: one trigram index over every place's own name, one
-- (below) over every localized name. Query with search_key % geo_search_key($1).
CREATE INDEX places_search_key ON places USING gin (search_key gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
-- Localized names are searched within one locale (the request's), so the
-- index leads with locale (btree_gin) and serves both
-- "locale = $2 AND search_key % ..." and search_key alone.
CREATE INDEX place_names_locale_search_key ON place_names USING gin (locale, search_key gin_trgm_ops);
-- +goose StatementEnd

-- +goose StatementBegin
-- The application only reads reference data; it changes through
-- migrations. Default privileges granted write access, so revoke first.
REVOKE ALL ON places, countries, time_zones, subdivisions, cities, place_names FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT ON places, countries, time_zones, subdivisions, cities, place_names TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE place_names, cities, subdivisions, time_zones, countries, places;
-- +goose StatementEnd

-- +goose StatementBegin
DROP FUNCTION geo_search_key(text);
-- +goose StatementEnd
