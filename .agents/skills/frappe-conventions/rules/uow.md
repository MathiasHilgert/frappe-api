# Unit of work and transaction rules (UOW)

- UOW-01 · One keyed unit of work per module; handlers only stage
- UOW-02 · The transaction opens at commit, in a fixed order
- UOW-03 · Set-based writes only inside the unit's transaction
- UOW-04 · Every aggregate is versioned by `xmin`
- UOW-05 · Constraint names map to rejections in the unit of work
- UOW-06 · DbContexts are stateless and pooled
- UOW-07 · No `EnableRetryOnFailure`
- UOW-08 · Nothing external inside the transaction; what must happen leaves through the outbox
- UOW-09 · After-commit handlers are for losable effects only

## UOW-01 · One keyed unit of work per module; handlers only stage

- **Rule:** Each module registers one unit of work over its own context with `AddUnitOfWork<TContext>(Module)`, keyed by the module name. Handlers and
  Application ports stage changes (`Add`, aggregate transitions) and never save; the pipeline commits once per command.
- **Why:** One commit per command keeps aggregates, outbox rows and events atomic; a port that saves splits the command into partial commits.
- **Enforced by:** LayeringTests `Application_ports_leave_saving_to_the_unit_of_work`; `UnitOfWorkPipelineTests`
  `A_succeeding_command_is_committed_once`, `A_command_of_a_module_without_a_unit_of_work_fails_naming_the_module_instead_of_saving_nothing`.
- **Good:** `.AddUnitOfWork<GeoDbContext>(GeoDbContext.Module)`; `void Add(HolidayCalendar calendar); // the unit of work saves it`. **Bad:** `Task
  SaveAsync(...)` on an Application port; Notification with no unit of work.
- **Status:** in force.

## UOW-02 · The transaction opens at commit, in a fixed order

- **Rule:** The handler's reads and calls to other systems run with no transaction open. `CommitAsync` then runs, in order: in-unit
  `IDomainEventHandler`s → one `SaveChanges` (aggregates and outbox rows) → commit → `IAfterCommitDomainEventHandler`s. Inside an inbox consumer's
  transaction the unit nests in a savepoint and runs no after-commit handler. Only platform pieces (`EfUnitOfWork`, `InboxConsumer`) and migrations
  begin transactions; never a handler or module adapter.
- **Why:** No connection or lock is held while a handler waits on the network, and the outbox is written in the same transaction as the change it
  announces.
- **Enforced by:** `UnitOfWorkPipelineTests`; review for `BeginTransactionAsync` outside `EfUnitOfWork`.
- **Good:** `await using (var transaction = await db.Database.BeginTransactionAsync(ct)) { events = await SaveAsync(ct); await
  transaction.CommitAsync(ct); } await AfterCommitAsync(events);` (`EfUnitOfWork`). **Bad:** `await db.Database.BeginTransactionAsync(ct)` at the top
  of a handler, then an HTTP call.
- **Status:** in force.

## UOW-03 · Set-based writes only inside the unit's transaction

- **Rule:** In a command, `ExecuteUpdate`/`ExecuteDelete` run only from an `IDomainEventHandler` (or another commit step), so they join the unit's
  transaction. Never call them from the handler body.
- **Why:** In the handler body no transaction is open yet: the statement autocommits on its own and survives a later rollback.
- **Enforced by:** review.
- **Good:** `RevokeSessionsOnPasswordReset : IDomainEventHandler<...>` calling `sessions.RevokeAllAsync(...)` (one guarded `UPDATE`). **Bad:** a
  command handler calling `sessions.RevokeAllAsync(...)` before returning `Unit.Value`.
- **Status:** in force.

## UOW-04 · Every aggregate is versioned by `xmin`

- **Rule:** Every aggregate's configuration maps Postgres `xmin` as its row version. When an aggregate has independent writers (a reset and a rehash,
  a rename and a rehash), keep `xmin` and let the module's commit rules (UOW-05) read the stored row back and repair a conflict on fields the command
  never depended on; never add per-column tokens for that. A lost conflict surfaces as an `ICommitRejection` (CQRS-05). No `SELECT … FOR UPDATE`
  fences.
- **Why:** One optimistic token per aggregate, the convention Npgsql documents, catches every lost update with no schema column and no hand-kept list
  of fields.
- **Enforced by:** ArchTest (planned) over the EF model: every `AggregateRoot` entity has a row-version property.
- **Good:** `builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();` (`HolidayCalendarConfiguration`). **Bad:** `owner.Property(o =>
  o.PasswordChangedAt).IsConcurrencyToken();` with no `xmin` and no justification; `FOR UPDATE` fences in `IdentityDbContext`.
- **Exception: a counted limit under concurrency (Businesses cap, D10 "Candado + excepción anotada"):** `xmin` protects a row that exists; a limit
  over a count of rows ("at most N businesses per owner") has no row to version, and two commits that each read `N-1` would both pass. The one allowed
  lock is a **transaction-scoped advisory lock** (`pg_advisory_xact_lock(<module namespace int>, hashtext(<owner key>))`) taken inside the commit's
  transaction by an in-unit `IDomainEventHandler` (UOW-02), then the count in a second statement, rejecting with an `ICommitRejection`. It is not
  `SELECT … FOR UPDATE` on rows, it releases itself when the transaction ends, and it serializes only the creations of one owner. Each use is marked
  in code `// Exception to UOW-04: <reason>` (`EfBusinessCapacity`, `EnforceBusinessCap`) and tested with parallel commits that would pass without it.
- **Child-only changes:** `xmin` only sees the root row. An aggregate whose change can touch only a child collection (aliases, entries) bumps a root
  `Revision` on every change, as `HolidayCalendar` does, so `xmin` catches it; its configuration says so.
- **Status:** in force.

## UOW-05 · Modules register commit rules; the unit of work applies them

- **Rule:** Each module registers commit rules keyed by a constraint name (`ux_…`, `ck_…`, `fk_…`), a lost concurrency check or a Postgres state; a
  rule rejects (the module's `ICommitRejection`) or repairs and saves again, at most a fixed number of times, after which the answer is 409
  `platform.persistence.commit_conflict`. The unit of work evaluates them; a save made outside a unit gets only the rejection, through the platform
  interceptor. A `DEFERRABLE` constraint is checked at COMMIT, after the save, so it is mapped with `OnDeferred<TRejection>(constraint, …)`, which can
  only reject (nothing is left to repair at commit): a unit whose module registered one runs `SET CONSTRAINTS ALL IMMEDIATE` in a savepoint right
  before COMMIT, so the failure reaches the rule while the transaction still reads (released on its own transaction, rolled back inside an owner's so
  the owner keeps its deferral). A deferrable constraint with no `OnDeferred` rule answers 500 when it fails: register the rule, or keep the
  constraint `INITIALLY IMMEDIATE`. DbContexts never override `SaveChanges`/`SaveChangesAsync`.
- **Why:** Race translation stays declarative, one line per constraint, instead of hundreds of lines of exception sniffing per context.
- **Enforced by:** ArchTest (planned): no `DbContext` subclass overrides `SaveChanges*`.
- **Good:** (planned API) `.AddCommitRejections<GeoDbContext>(map => map.On("ux_subdivision_aliases_country_alias", e => new
  SubdivisionAliasRaceLostException(...)))`. **Bad:** `public override async Task<int> SaveChangesAsync(...)` with `catch (DbUpdateException ...)` in
  `IdentityDbContext` and `GeoDbContext`.
- **Status:** in force.

## UOW-06 · DbContexts are stateless and pooled

- **Rule:** A DbContext holds mapping only: no instance fields, no per-request state. Every module registers it with `AddDbContextPool`.
- **Why:** Pooling cuts allocation on every request, and it is only safe when nothing leaks from one use to the next.
- **Enforced by:** ArchTest (planned): `DbContext` subclasses declare no instance fields; review of `ModuleDatabaseRegistration`.
- **Good:** `services.AddDbContextPool<TContext>((provider, options) => configure(...))`. **Bad:** `.AddDbContext<TContext>(...)`
  (`ModuleDatabaseRegistration`); `private readonly List<...> _profileFences = [];` in `IdentityDbContext`.
- **Status:** in force.

## UOW-07 · No `EnableRetryOnFailure`

- **Rule:** Never configure an EF execution strategy with retries. Transient failures are retried by message redelivery, outbound HTTP resilience or
  the client running the command again.
- **Why:** An execution strategy replays a user-initiated transaction outside the pipeline and conflicts with the unit's own transaction and
  savepoints.
- **Enforced by:** BannedApiAnalyzers (planned): `NpgsqlDbContextOptionsBuilder.EnableRetryOnFailure`.
- **Good:** `options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime())`. **Bad:** `npgsql.EnableRetryOnFailure(3)`.
- **Status:** in force.

## UOW-08 · Nothing external inside the transaction; what must happen leaves through the outbox

- **Rule:** Effects that must happen once the command commits (mails, third-party calls, other modules) leave through the outbox as integration
  events. Only protective limits and counters may run before commit, in Valkey, with a TTL and an explicit refund when the command fails. Nothing
  external runs while the transaction is open.
- **Why:** An external call cannot roll back; done before commit it fires for a command that never happened, done inside it holds locks for a network
  round trip.
- **Enforced by:** review.
- **Durable effect on a failure answer:** an effect that must survive even though the command answers a failure (its unit rolls back) goes through a
  port whose adapter runs a separate command in its own scope and unit, never sharing the caller's connection (the pending-login resend).
  LayeringTests does not police this, so review must.
- **Good:** reserve a login attempt in the Valkey window counter, refund it when the handler answers a failure; the code mail goes through
  `VerificationCodeIssued` in the outbox. **Bad:** calling the mail provider from a command handler or from an `IDomainEventHandler`.
- **Status:** in force.

## UOW-09 · After-commit handlers are for losable effects only

- **Rule:** `IAfterCommitDomainEventHandler` runs only effects the system can lose: metrics, a quick local cache eviction. It finishes within the
  unit's 10 s bound and never fails the command. Anything that must happen goes through the outbox, or the handler's XML doc states its durable
  backstop. Business-success counters are counted after commit (`CountOnCommit`, planned).
- **Why:** After commit there is no transaction to roll back; a crash or timeout there loses the effect silently.
- **Enforced by:** review.
- **Good:** an after-commit handler evicting the territory cache, with integration-event eviction as the backstop. **Bad:** an after-commit handler
  that sends the only welcome mail.
- **Status:** in force.
