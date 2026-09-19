-- Started sign-ups: the address as entered, kept between "start" and "complete" so identity's listener can mail the
-- code. Not tenant-scoped: a sign-up exists before any business (Decision Log). Holds personal data; old rows are
-- purged by a later ticket.
create table identity.sign_up (
    id uuid primary key,
    email text not null,
    -- KeyedDigests.subjectOf("identity.email", canonical address): one sign-up per address, found without the address.
    email_subject uuid not null,
    locale text not null,
    started_at timestamptz not null,
    version bigint not null,
    constraint sign_up_email_subject_key unique (email_subject)
);
