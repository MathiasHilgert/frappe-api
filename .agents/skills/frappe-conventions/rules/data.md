# Data and schema rules (DATA)

- DATA-01 · One schema per module, its migration history inside it
- DATA-02 · Ids are UUIDv7 made by the application
- DATA-03 · Reference data uses natural keys
- DATA-04 · snake_case names, set explicitly
- DATA-05 · Every key, index and check carries our lower-case prefix
- DATA-06 · A closed set is short text guarded by a CHECK
- DATA-07 · Multi-column invariants are CHECK constraints on the table
- DATA-08 · Lifecycle is status and instants; delete is a real delete
- DATA-09 · Every growing table has a retention job
- DATA-10 · Translations live in `<entity>_names`
- DATA-11 · Optimistic concurrency uses xmin by default
- DATA-12 · Third normal form; copies are documented; jsonb only for whole documents
- DATA-13 · Every module has an outbox and an inbox
- DATA-14 · No database object is shared across modules

## DATA-01 · One schema per module, its migration history inside it

- **Rule:** A module's tables, functions and migration history (`<schema>.__ef_migrations_history`) live in its own schema, set with
  `HasDefaultSchema` and `UseModulePostgres`. The schema name is a `Schema` constant on the module's DbContext.
- **Why:** The schema is the ownership boundary: grants, purges and migrations stay per module, so one module can change without migrating another.
- **Enforced by:** `MigratorTests` (each module role reads and writes its own schema and nothing else) | review.
- **Good:** `modelBuilder.HasDefaultSchema(Schema);` with `public const string Schema = "mailing";`. **Bad:** `ToTable("sessions", "public")`.
- **Status:** in force.

## DATA-02 · Ids are UUIDv7 made by the application

- **Rule:** Surrogate ids are `Guid.CreateVersion7()` values made in the domain; map them with `ValueGeneratedNever()`. Never `Guid.NewGuid()`,
  database defaults or serial ids for entities (an identity column only for ordering, like `outbox_messages.sequence`).
- **Why:** The id exists before the insert, so events and responses can carry it; v7 is time-ordered, so B-tree inserts stay local.
- **Enforced by:** review (a `Guid.NewGuid` ban in BannedApiAnalyzers is a candidate, not planned).
- **Good:** `builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();`. **Bad:** `.HasDefaultValueSql("gen_random_uuid()")`.
- **Status:** in force.

## DATA-03 · Reference data uses natural keys

- **Rule:** Reference catalogs key on their standard code: ISO codes as `character(n)` (`currencies.code`, `languages.code`); children key on the
  parent's code plus their own part (`currency_names (currency_code, language)`).
- **Why:** Codes are stable, short, and what clients and other modules already hold; no join to translate a surrogate.
- **Enforced by:** `eng/squawk.toml` (`ban-char-field` excluded on purpose) | review.
- **Good:** `builder.HasKey(c => c.Code); builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(3).IsFixedLength();`. **Bad:**
  `currencies.id uuid` plus a unique `code`.
- **Status:** in force.

## DATA-04 · snake_case names, set explicitly

- **Rule:** Tables are plural snake_case, columns snake_case, both written with `ToTable`/`HasColumnName` in the entity's configuration. No
  naming-convention plugin.
- **Why:** The stored name is a contract with migrations, raw SQL and grants; an explicit name never changes because a C# property was renamed.
- **Enforced by:** review.
- **Good:** `builder.Property(s => s.LastSeenAt).HasColumnName("last_seen_at");`. **Bad:** relying on EF's `"LastSeenAt"` or
  `UseSnakeCaseNamingConvention()`.
- **Status:** in force.

## DATA-05 · Every key, index and check carries our lower-case prefix

- **Rule:** Name every constraint and index explicitly, always lower case: `pk_<table>`, `fk_<table>_<column>`, `ux_<table>_<rule>`,
  `ix_<table>_<columns>`, `ck_<table>_<rule>`. A name other code reads (raw SQL, a migration, a constraint translator) is a `public const`.
- **Why:** One readable scheme lets a constraint violation map to its rule by name; EF defaults (`PK_`, `FK_`) and Postgres `*_key` names break that
  mapping. A rename is metadata only.
- **Enforced by:** review.
- **Good:** `public const string PendingIndex = "ix_outbox_messages_relayable";`. **Bad:** `table.PrimaryKey("PK_regions", ...)`,
  `"sessions_token_hash_key"`.
- **Status:** in force.

## DATA-06 · A closed set is short text guarded by a CHECK

- **Rule:** Store a closed set as short text with a `CHECK (col IN (...))`; the converter, the CHECK list and the column length read the same
  `StoredEnum<TEnum>` source (`Converter`, `CheckList`, `MaxLength`), and a test pins each enum's spellings. Stored
  values are lower snake_case (`email_change`, `tourist_non_working_day`) in every module, never PascalCase or the enum's `ToString()`; the wire name
  of a value lives apart (an Application `NameOf`), so the stored spelling and the API code can change independently. Never an int-backed enum, never
  a string with no CHECK.
- **Why:** A row with an unknown value is refused at the write, not discovered when the reader's switch throws.
- **Enforced by:** review.
- **Good:** `.HasConversion(StoredEnum<Surface>.Converter).HasMaxLength(StoredEnum<Surface>.MaxLength)` with
  `table.HasCheckConstraint("ck_<table>_surface", $"surface IN ({StoredEnum<Surface>.CheckList})");`. **Bad:**
  `.HasConversion<string>().HasMaxLength(32)` alone.
- **Status:** in force.

## DATA-07 · Multi-column invariants are CHECK constraints on the table

- **Rule:** An invariant across columns of one row is a CHECK declared through the table builder: `ToTable(name, t => t.HasCheckConstraint(...))`.
  Name and predicate are constants next to the model.
- **Why:** The database refuses the row a bug or a hand edit would write; the entity-level overload is obsolete (CS0618).
- **Enforced by:** compiler (CS0618 under warnings as errors) | `OutboxInvariantsTests`.
- **Good:** `outbox.ToTable(Table, t => t.HasCheckConstraint(PublishedSettled, PublishedSettledSql));`. **Bad:** `builder.HasCheckConstraint(...)` on
  the entity, or the invariant only in C#.
- **Status:** in force.

## DATA-08 · Lifecycle is status and instants; delete is a real delete

- **Rule:** Model a lifecycle with a status and the instants that move it (`revoked_at`, `idle_expires_at`); remove with a hard delete. Synced
  reference rows that leave their source get `retired_at`. No `deleted_at`, no `is_deleted`.
- **Why:** Soft delete leaks into every query and every unique index, and keeps personal data no one may read.
- **Enforced by:** review.
- **Good:** `sessions.revoked_at`, `idle_expires_at`, purged by `ix_sessions_ended_at`; `localities.retired_at`. **Bad:** `is_deleted boolean`
  filtered by a global query filter.
- **Status:** in force.

## DATA-09 · Every growing table has a retention job

- **Rule:** A table that grows with traffic or time ships with its purge: a Worker job deleting in batches (PERF-09) after a retention set in
  validated options, plus the index the purge reads.
- **Why:** An unbounded table slows every index and vacuum and holds data longer than anyone promised.
- **Enforced by:** review | each purge's tests (`CredentialPurgeTests`) and options validators (`MessageRetentionOptionsValidator`).
- **Good:** `MessagePurger` with `MessageRetentionOptions.OutboxRetention`; `MailOutcomePurger`. **Bad:** an audit table with no purge
  (`orders.sync_runs`).
- **Status:** in force.

## DATA-10 · Translations live in `<entity>_names`

- **Rule:** Translated text goes in `<entity>_names` keyed `(<owner>_code, language)`; `language` is a BCP 47 tag stored through `StoreLanguageTags`
  with length `LanguageTag.StorageLength`.
- **Why:** A new language is rows, not columns; one length everywhere keeps every tag storable.
- **Enforced by:** review.
- **Good:** `region_names (region_code, language, text)`. **Bad:** `name_es`, `name_en` columns; `.HaveMaxLength(35)` hard-coded.
- **Status:** in force.

## DATA-11 · Optimistic concurrency uses xmin by default

- **Rule:** Same as UOW-04 (canonical card in `uow.md`); read it there.
- **Status:** see UOW-04.

## DATA-12 · Third normal form; copies are documented; jsonb only for whole documents

- **Rule:** Design in 3NF. A copied or derived column (`search_name`, `localities.region_code`) carries a comment in its configuration: what it
  copies, which query needs it, who keeps it in sync. `jsonb` only for a document read and written whole, never filtered or joined inside; a field a
  query needs is a column.
- **Why:** Undocumented copies drift silently; filtering inside jsonb defeats indexes and constraints.
- **Enforced by:** review.
- **Good:** `outbox_messages.data jsonb` (read whole by the relay). **Bad:** `WHERE data->>'status' = 'x'`; `LocalityRowConfiguration` copying
  `subdivision_code` and `search_name` with no comment.
- **Status:** in force.

## DATA-13 · Every module has an outbox and an inbox

- **Rule:** Every module's DbContext calls `AddOutbox()` and `AddInbox()` and its messaging registration wires the outbox interceptor and relay, from
  day one, even when it publishes nothing yet.
- **Why:** Uniform messaging: the first published event needs no infrastructure change, and the purge, metrics and alerts apply unchanged.
- **Enforced by:** `MigratorTests` (every module outbox gets the relay index and the four invariants) | review for the wiring.
- **Good:** `OrdersMessaging` registers the relay. **Bad:** `MailingMessaging` has the tables but no relay or interceptor.
- **Status:** in force.

## DATA-14 · No database object is shared across modules

- **Rule:** No table, view, function or foreign key crosses schemas. Another module's data arrives through its Contracts (query port) or a local copy
  fed by its integration events.
- **Why:** A cross-schema object couples migrations, grants and outages of two modules.
- **Enforced by:** `MigratorTests` (each role reads and writes its own schema and nothing else) | review.
- **Good:** Mailing keeps its own `mail_outcomes`. **Bad:** `REFERENCES billing.principals(id)` from `orders`.
- **Status:** in force.
