-- Test-only tenant-scoped table, following the RLS template of persistence.md. The setting is read with missing_ok
-- and an empty value counts as none: a pooled connection that ran a transaction-local set_config returns '' for it
-- afterwards, which must hide every row instead of failing the cast.
create table fixture.tenant_probe (
    id uuid primary key,
    tenant_id uuid not null,
    label text not null
);

create index tenant_probe_tenant_id_idx on fixture.tenant_probe (tenant_id);

alter table fixture.tenant_probe enable row level security;
alter table fixture.tenant_probe force row level security;

create policy tenant_isolation on fixture.tenant_probe
    using (tenant_id = (select nullif(current_setting('app.tenant_id', true), '')::uuid))
    with check (tenant_id = (select nullif(current_setting('app.tenant_id', true), '')::uuid));
