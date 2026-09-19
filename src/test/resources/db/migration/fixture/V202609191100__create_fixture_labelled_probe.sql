-- Test-only: a unique label checked at commit (deferred), so a test can make a transaction fail on commit after
-- every statement succeeded.
create table fixture.labelled_probe (
    id uuid primary key,
    label text not null,
    constraint labelled_probe_label_unique unique (label) deferrable initially deferred
);
