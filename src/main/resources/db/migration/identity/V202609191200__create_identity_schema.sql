-- Schema owned by the identity module. Flyway runs as frappe_owner, so frappe_app
-- gets USAGE through the default privileges set by docker/postgres/initdb.
create schema if not exists identity;
