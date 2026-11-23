# Migration rules (MIG)

- MIG-01 · Every model change ships its migration
- MIG-02 · New migration SQL passes squawk
- MIG-03 · DDL on a live table starts with `lock_timeout`
- MIG-04 · CHECK constraints are added NOT VALID, then validated
- MIG-05 · Indexes are built and dropped CONCURRENTLY
- MIG-06 · Migrations use literals only and never read model types
- MIG-07 · Breaking column changes go expand/contract
- MIG-08 · The migration documents locks, rerunnability and rollout class
- MIG-09 · Adding uniqueness checks for duplicates first and never deletes data
- MIG-10 · The Migrator owns DDL; login roles are narrow
- MIG-11 · Module roles get DML on their own schema, never DDL
- MIG-12 · Narrower roles get column grants and pinned SECURITY DEFINER functions
- MIG-13 · CI upgrades the base database with the branch's Migrator
- MIG-14 · Reproduce squawk locally outside the repo

### MIG-01 · Every model change ships its migration
- **Rule:** A change to a DbContext's model ships, in the same PR, the migration that brings the database to it (`dotnet ef migrations add`), Designer
  and snapshot included.
- **Why:** A model ahead of its schema fails at runtime on the first query that touches the difference.
- **Enforced by:** `dotnet ef migrations has-pending-model-changes` (Compatibility workflow).
- **Good:** `20261116143839_IndexParkedOutboxRows.cs` beside the `OutboxModel` change. **Bad:** a new `HasIndex` with no migration.
- **Status:** in force.

### MIG-02 · New migration SQL passes squawk
- **Rule:** The SQL of every new migration passes squawk 2.72.0 with `eng/squawk.toml` (pg 18, `assume_in_transaction`). An unavoidable finding gets a
  per-statement `-- squawk-ignore <rule>` with the reason in a comment beside it; otherwise the PR needs the `risky migration approved` label.
- **Why:** squawk catches the statements that lock tables or lose data before they reach a live database.
- **Enforced by:** squawk (Compatibility workflow, `.github/scripts/migration-sql.sh`).
- **Good:** `"-- squawk-ignore ban-concurrent-index-creation-in-transaction\nDROP INDEX CONCURRENTLY IF EXISTS ..."` with `suppressTransaction: true`.
  **Bad:** a file-wide ignore, or one with no reason.
- **Status:** in force.

### MIG-03 · DDL on a live table starts with `lock_timeout`
- **Rule:** Every transactional DDL on an existing table starts with `SET LOCAL lock_timeout = '5s';` (`'2s'` for an ACCESS EXCLUSIVE lock on a hot
  table: outbox, inbox, sessions, localities).
- **Why:** A DDL waiting for its lock queues every reader and writer behind it; failing fast and retrying beats an outage.
- **Enforced by:** review (squawk's `require-timeout-settings` is excluded in `eng/squawk.toml`).
- **Good:** `migrationBuilder.Sql("SET LOCAL lock_timeout = '2s';");` first in `RequireSessionPasswordStamp.Up` (sessions, hot). **Bad:** `ALTER TABLE
  identity.sessions ADD CONSTRAINT ...` with no lock timeout.
- **Status:** in force.

### MIG-04 · CHECK constraints are added NOT VALID, then validated
- **Rule:** Add a CHECK `NOT VALID` inside the transaction (`DROP CONSTRAINT IF EXISTS` + `ADD` in one `ALTER`), then `VALIDATE CONSTRAINT` in its own
  statement with `suppressTransaction: true`, in the same migration by default. Use two migrations when existing rows may break it and need cleanup
  first, or on a hot or large table (outbox, inbox, sessions, localities, or more than ~1M rows expected).
- **Why:** `NOT VALID` takes a short lock; the validating scan then runs without the ALTER's lock held.
- **Enforced by:** `MigratorTests` (invariants validated; a breaking row fails the migration naming it) | review.
- **Good:** hot table, two migrations: `StampSessionsWithPassword` adds `ck_sessions_password_stamp ... NOT VALID`, `ValidateSessionPasswordStamp`
  validates it. **Bad:** `ADD CONSTRAINT ck_x CHECK (...)` validated under the ALTER's lock.
- **Status:** in force.

### MIG-05 · Indexes are built and dropped CONCURRENTLY
- **Rule:** On an existing table, `CREATE`/`DROP INDEX CONCURRENTLY`, one statement per `Sql` call, `suppressTransaction: true`, each create preceded
  by `DROP INDEX CONCURRENTLY IF EXISTS` so a failed build (an invalid index) is cleared on rerun.
- **Why:** A plain build blocks writes for the whole build; a concurrent one cannot share a transaction or a batch.
- **Enforced by:** squawk (`require-concurrent-index-creation`, `ban-concurrent-index-creation-in-transaction`) | review.
- **Good:** `IndexParkedOutboxRows.Up`. **Bad:** `migrationBuilder.CreateIndex(...)` on `sessions`; `CREATE UNIQUE INDEX CONCURRENTLY` with no `DROP
  INDEX CONCURRENTLY IF EXISTS` before it.
- **Status:** in force.

### MIG-06 · Migrations use literals only and never read model types
- **Rule:** A migration writes table names, constraint names, predicates and index definitions as literals. It never references configuration, model
  or platform types (`SessionConfiguration`, `OutboxModel`), not even for a name.
- **Why:** A migration is frozen history; reading a constant rewrites what an old migration does when the model later changes.
- **Enforced by:** `MigrationTests` (a source scan of the hand-written migration files, comments included; C# inlines constants, so an assembly check
  cannot see them).
- **Good:** `IndexParkedOutboxRows`: `"CREATE INDEX CONCURRENTLY ix_outbox_messages_parked ON geo.outbox_messages (sequence) WHERE ..."`. **Bad:**
  `private const string Table = "geo." + OutboxModel.Table;`, `{SessionConfiguration.PasswordStampCheck}`.
- **Status:** in force.

### MIG-07 · Breaking column changes go expand/contract
- **Rule:** A new required column lands in steps: add it nullable without a default → backfill → `CHECK (col IS NOT NULL) NOT VALID` → validate → `SET
  NOT NULL` in a later migration. Renames and drops add the new shape first and remove the old one only after no running binary reads it.
- **Why:** Each step is compatible with the binary before and after it, so replicas can roll.
- **Enforced by:** review.
- **Good:** `StampSessionsWithPassword` → `ValidateSessionPasswordStamp` → `RequireSessionPasswordStamp`. **Bad:** `AddColumn(nullable: false,
  defaultValue: ...)` on a live table in one step.
- **Status:** in force.

### MIG-08 · The migration documents locks, rerunnability and rollout class
- **Rule:** The migration's XML doc states the locks it takes and for how long, whether it is rerunnable after a partial failure, and its rollout
  class: `rolling-safe`; `rolling with a brief stall` (an ACCESS EXCLUSIVE wait bounded by `lock_timeout`, with no required order between old and new
  binaries); or `coordinated cutover` (with the steps). A non-rolling-safe migration needs a "Rollout" section in its PR, which release-please carries
  to the CHANGELOG.
- **Why:** The operator deploying it reads the migration and the CHANGELOG, not the PR thread.
- **Enforced by:** review.
- **Good:** `StampSessionsWithPassword` summary: locks, backfill cost, "ships as a coordinated cutover (stop the Api, migrate, start the new binary)".
  **Bad:** `/// <summary>Adds a column.</summary>`; a summary with no rollout class.
- **Status:** in force.

### MIG-09 · Adding uniqueness checks for duplicates first and never deletes data
- **Rule:** Before a unique index or constraint, the migration looks for duplicates and raises an exception naming them. It never deletes or merges
  rows itself. The check and the build are rerunnable (`IF EXISTS` in Up and Down).
- **Why:** Which duplicate to keep is a business decision; a failed concurrent build otherwise leaves an invalid index.
- **Enforced by:** review.
- **Good:** the check in `AddManualAliasCountryUniqueness`: `RAISE EXCEPTION 'Manual subdivision aliases shared by two subdivisions of a country: %.
  ...'`. **Bad:** `DELETE FROM ... WHERE ctid NOT IN (SELECT min(ctid) ...)`; that same migration's build and `Down` without `IF EXISTS`.
- **Status:** in force.

### MIG-10 · The Migrator owns DDL; login roles are narrow
- **Rule:** Only the Migrator runs DDL, as the schema owner, under an advisory lock. It creates every login role `NOINHERIT NOCREATEDB NOCREATEROLE`
  with a SCRAM verifier, and sets `statement_timeout` and `idle_in_transaction_session_timeout` on each from `DatabaseOptions`.
- **Why:** A compromised or buggy Api cannot change the schema; two Migrators started together cannot race.
- **Enforced by:** `MigratorTests` (`Only_a_SCRAM_verifier_reaches_the_server...`, `Two_Migrators_started_together_both_succeed`,
  `Every_login_role_gets_the_configured_session_timeouts`).
- **Good:** `ModuleRoles`. **Bad:** the Api calling `Database.MigrateAsync()`.
- **Status:** in force.

### MIG-11 · Module roles get DML on their own schema, never DDL
- **Rule:** A module's login role gets DML on its schema's tables and default privileges for future ones; never DDL, never the migration history;
  `PUBLIC` loses access to the schema.
- **Why:** Least privilege: a module cannot read another's data or rewrite its own history.
- **Enforced by:** `MigratorTests` (`The_<Module>_role_reads_and_writes_<Module>_data_and_nothing_else`).
- **Good:** `ModuleRoles` grants. **Bad:** `GRANT ALL ON SCHEMA identity TO identity_app`.
- **Status:** in force.

### MIG-12 · Narrower roles get column grants and pinned SECURITY DEFINER functions
- **Rule:** A role that needs less than its module's DML (a relay, a purge) gets column-level `UPDATE` and `EXECUTE` on `SECURITY DEFINER` functions
  with `search_path` pinned and `EXECUTE` revoked from `PUBLIC`.
- **Why:** The relay can publish and purge but cannot read or change business rows.
- **Enforced by:** `MigratorTests` (Identity and Businesses relay roles) | `CredentialPurgeTests` | the `RelayRoles` statement test.
- **Good:** `RelayRoles.GrantAsync` (Frappe.Messaging) called by `IdentityMigration` and `BusinessesMigration`; the module passes only its schema,
  role and purge functions. **Bad:** the relay logging in as the module role.
- **Status:** in force.

### MIG-13 · CI upgrades the base database with the branch's Migrator
- **Rule:** The Compatibility workflow migrates a database built from the base branch with the branch's Migrator; a PR that cannot upgrade it does not
  merge.
- **Why:** Production is always the base plus this migration, never a fresh database.
- **Enforced by:** Compatibility workflow.
- **Good:** the upgrade job green. **Bad:** testing migrations only on an empty database.
- **Status:** in force.

### MIG-14 · Reproduce squawk locally outside the repo
- **Rule:** Lint locally with `.github/scripts/migration-sql.sh <base-branch> "$(mktemp -d)"` and `npx --yes squawk-cli@2.72.0 --config
  eng/squawk.toml <files>`; the SQL directory never lives in the worktree.
- **Why:** Same script and version as CI, and no generated SQL left in the change.
- **Enforced by:** review (a just recipe is a candidate, not planned).
- **Good:** `out="$(mktemp -d)"; .github/scripts/migration-sql.sh main "$out"`. **Bad:** `migration-sql.sh main ./tmp-sql`.
- **Status:** in force.
