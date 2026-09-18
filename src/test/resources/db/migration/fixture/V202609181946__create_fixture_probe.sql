-- Test-only module migration: no GRANT here on purpose, frappe_app access must come
-- from the default privileges of frappe_owner.
create schema if not exists fixture;

create table fixture.probe (
    id uuid primary key,
    label text not null
);
