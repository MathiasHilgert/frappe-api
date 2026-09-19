-- People and their recovery codes, one aggregate. Not tenant-scoped: one person belongs to many businesses and is found
-- before any business is chosen (Decision Log). Erased rows keep the id only.
create table identity.person (
    id uuid primary key,
    email text,
    status text not null check (status in ('ACTIVE', 'ERASED')),
    password_hash text,
    credential_epoch bigint not null,
    given_name text,
    family_name text,
    preferred_locale text,
    terms_version text,
    privacy_version text,
    legal_accepted_at timestamptz,
    registered_at timestamptz not null,
    erased_at timestamptz,
    version bigint not null,
    -- An active person always holds everything registering gave them; only erasure clears it.
    constraint person_active_complete check (status <> 'ACTIVE' or (email is not null and password_hash is not null
        and given_name is not null and family_name is not null and terms_version is not null
        and privacy_version is not null and legal_accepted_at is not null))
);

-- One person per address in any case; erased people hold none.
create unique index person_email_key on identity.person (lower(email)) where email is not null;

-- Only keyed digests of person id and code (KeyedDigests "identity.recovery-code"): a dump yields no usable code.
create table identity.recovery_code (
    id uuid primary key,
    person_id uuid not null references identity.person (id) on delete cascade,
    digest text not null,
    used_at timestamptz,
    constraint recovery_code_digest_key unique (digest)
);

create index recovery_code_person_id_idx on identity.recovery_code (person_id);
