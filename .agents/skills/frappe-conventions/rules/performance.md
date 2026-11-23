# Performance rules (PERF)

- PERF-01 · Pools and timeouts are bounded per process and per role
- PERF-02 · No EF execution-strategy retries
- PERF-03 · A transaction only to commit, one SaveChanges, no network inside
- PERF-04 · Every read states its tracking explicitly
- PERF-05 · A guarded set-based write repeats the domain guards, inside the transaction
- PERF-06 · A complex read is one statement
- PERF-07 · A partial index predicate and its query filter are one constant
- PERF-08 · Drop indexes no query reads
- PERF-09 · Purges delete in small keyed batches
- PERF-10 · Retired: the FusionCache defaults moved to CACHE-11
- PERF-11 · Count the round trips of a request
- PERF-12 · State the behavior at 10× and 100× the data

## PERF-01 · Pools and timeouts are bounded per process and per role

- **Rule:** Every module database uses `DatabaseOptions`: `MaxPoolSize` 6 per process per database, `CommandTimeout` 35 s above the role's
  `statement_timeout` 30 s, `idle_in_transaction_session_timeout` at least 1 min. A connection string setting wins over the defaults. A new pool (job
  store, readiness) gets an explicit limit.
- **Why:** Replicas × pools must stay under `max_connections`; the server cancels a runaway statement before the client gives up.
- **Enforced by:** `DatabaseOptionsValidator` | `MigratorTests` (`Every_login_role_gets_the_configured_session_timeouts`).
- **Good:** `ModuleDatabase.WithConnectionLimits`. **Bad:** a raw `NpgsqlDataSource` with the default pool of 100.
- **Status:** in force.

## PERF-02 · No EF execution-strategy retries

- **Rule:** Never call `EnableRetryOnFailure` or a custom `IExecutionStrategy`. Retries come from message redelivery, outbound HTTP resilience or the
  client rerunning an idempotent request.
- **Why:** An execution strategy replays the whole unit, including effects that already left the process, and it forbids user transactions.
- **Enforced by:** review.
- **Good:** `ModuleDatabase`: `builder.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()...)`, no strategy. **Bad:**
  `npgsql.EnableRetryOnFailure(3)`.
- **Status:** in force.

## PERF-03 · A transaction only to commit, one SaveChanges, no network inside

- **Rule:** A command's writes are staged and written by the unit of work's single `SaveChanges` (outbox rows included) and committed together. Reads
  and calls to other systems happen before the transaction opens. Nothing but `EfUnitOfWork` and `InboxConsumer` calls `SaveChangesAsync`.
- **Why:** Short transactions hold locks and connections for milliseconds; one SaveChanges is one round trip batch and one atomic outcome.
- **Enforced by:** `LayeringTests` (`Application_ports_leave_saving_to_the_unit_of_work`) | review.
- **Good:** `EfUnitOfWork.SaveChangesAsync`. **Bad:** `await db.SaveChangesAsync(ct)` in `SnapshotSync` or `OrdersSeeding`.
- **Status:** in force.

## PERF-04 · Every read states its tracking explicitly

- **Rule:** A read port's query starts with `AsNoTracking()` and projects to a record or `*View`; a command repository returns tracked aggregates. No
  context-wide tracking default; the reviewer checks each query's choice.
- **Why:** Tracking a read costs memory and snapshot comparison for rows nobody saves, and it makes a later command in the same scope see a stale
  copy.
- **Enforced by:** review.
- **Good:** `db.Set<RegionRow>().AsNoTracking().Where(c => c.RetiredAt == null)`. **Bad:** `EfSubdivisionAliases.ListAsync` projecting views from a
  tracked `SingleAsync` row.
- **Status:** in force.

## PERF-05 · A guarded set-based write repeats the domain guards, inside the transaction

- **Rule:** Same as UOW-03 (canonical card in `uow.md`); read it there.
- **Status:** see UOW-03.

## PERF-06 · A complex read is one statement

- **Rule:** A read that ranks, pages or combines several filters runs as one SQL statement: a SQL function or `FromSql`, composed with LINQ only when
  EF translates the whole of it.
- **Why:** One round trip and one plan; a client-side loop multiplies latency by the data.
- **Enforced by:** `LocalitySearchTests` (`A_search_is_one_database_command`, `..._served_by_the_trigram_index`).
- **Good:** `orders.search_localities(...)` called through `FromSql`. **Bad:** load candidates, then rank in C#.
- **Status:** in force.

## PERF-07 · A partial index predicate and its query filter are one constant

- **Rule:** A partial or expression index's predicate and the filter of every query meant to use it come from one constant, written word for word in
  both. The migration freezes it as a literal (MIG-06) and a test pins the model to it.
- **Why:** Postgres uses a partial index only when the query's filter proves its predicate; a reworded filter silently scans.
- **Enforced by:** `MigratorTests` (`Every_module_outbox_gets_the_relay_index_without_parked_rows_and_the_parked_index_the_model_declares`).
- **Good:** `OutboxModel.PendingFilter` used by the index and by `OutboxRelay.ClaimSql`. **Bad:** index `WHERE published_at IS NULL`, query `WHERE NOT
  (published_at IS NOT NULL)`.
- **Status:** in force.

## PERF-08 · Drop indexes no query reads

- **Rule:** An index no query reads any more is dropped (concurrently, MIG-05) in the change that removes its last reader.
- **Why:** Each index costs every insert and update, vacuum time and memory.
- **Enforced by:** review.
- **Good:** `PurgeEndedCredentials` drops `ix_sessions_idle_expires_at`, which no query read. **Bad:** keeping `ix_outbox_messages_pending` after the
  relay moved to `ix_outbox_messages_relayable`.
- **Status:** in force.

## PERF-09 · Purges delete in small keyed batches

- **Rule:** A purge deletes by key from a `LIMIT n` subselect (default 1000 per statement), each batch its own short statement, filtered on an indexed
  column, until a batch comes back short; scheduled nightly by the Worker.
- **Why:** Short statements hold few locks and keep replication lag and vacuum steady; running twice at once only finds less.
- **Enforced by:** review | `CredentialPurgeTests`.
- **Good:** `MessagePurger`: `DELETE FROM ... WHERE id IN (SELECT id FROM ... WHERE published_at < {0} LIMIT {1})`. **Bad:** `DELETE FROM
  outbox_messages WHERE published_at < now() - interval '7 days'`.
- **Status:** in force.

## PERF-11 · Count the round trips of a request

- **Rule:** A plan or review states the database and Valkey round trips of each request it adds or changes; a hot path aims for one database command.
  No query inside a loop (N+1); batch or join instead. A test asserts the count where it matters.
- **Why:** Latency adds per round trip, and pool time is the scarce resource under load.
- **Enforced by:** review | command-count tests (`A_search_is_one_database_command`).
- **Good:** `EfSessions.FindForAuthenticationAsync`: session and owner in one joined query. **Bad:** `foreach (var id in ids) await
  db.Set<X>().FindAsync(id);`.
- **Status:** in force.

## PERF-12 · State the behavior at 10× and 100× the data

- **Rule:** A new query, table or job states what happens at 10× and 100× today's rows or traffic: the index it reads, whether it pages, what grows
  without bound and the retention that stops it (DATA-09).
- **Why:** A plan that is fine on seed data becomes a sequential scan or a full table lock in production.
- **Enforced by:** review.
- **Good:** "localities ~4M rows at 100×: served by the trigram index, paged by cursor". **Bad:** `ListAsync()` returning every row with no limit.
- **Status:** in force.
