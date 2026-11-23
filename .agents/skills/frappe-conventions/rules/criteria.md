# Decision criteria

Decision rules for where work lives and what it depends on. The plan records which criterion decided each choice, by id. Mechanisms these decisions
lead to: `architecture`, `events`, `caching`.

- CRIT-01 · Create a module only for a bounded context of its own
- CRIT-02 · Extract to platform on the second real use, without business rules
- CRIT-03 · Add a dependency only when it passes the dependency checklist
- CRIT-04 · The request does only what the client waits for
- CRIT-05 · Read another module through its query port; copy its data only with a reason
- CRIT-06 · Ask with a query port, react with an event; the announcer may ask the reactor read-only questions
- CRIT-07 · Cache only read-heavy, rarely changing data, with a stated budget
- CRIT-08 · Ephemeral state goes to Valkey behind its own port, never to the cache

### CRIT-01 · Create a module only for a bounded context of its own
- **Rule:** a new module needs all three: (1) its own language and rules, (2) data only it writes, (3) it can change or fail without dragging others.
  If it shares a transactional invariant with an existing aggregate, it is a feature of that module. The plan states the three criteria with one line
  of evidence each.
- **Why:** a wrong cut means distributed transactions or a module that is only a folder.
- **Enforced by:** review (plan step in `/forge`).
- **Good:** Notification: generic delivery, own mail-outcome table, Identity keeps working when mail fails. **Bad:** a "Profiles" module that must
  update `Principal` in the same commit.
- **Status:** in force.

### CRIT-02 · Extract to platform on the second real use, without business rules
- **Rule:** code moves to `src/Platform` only when a second module really needs it now and it holds no business rules (ARCH-18). Exception:
  cross-cutting pieces the rules already require go to platform on first use: `Page<T>` and its binding, `Idempotency-Key` handling, `CountOnCommit`.
- **Why:** a premature shared library couples modules to guesses; a duplicated cross-cutting piece drifts.
- **Enforced by:** review.
- **Good:** the outbox and relay in `Frappe.Messaging`, used by every publishing module. **Bad:** moving Geo's `HolidayCalendar` helpers to platform
  "because Payroll might need them".
- **Status:** in force.

### CRIT-03 · Add a dependency only when it passes the dependency checklist
- **Rule:** a new NuGet package or platform library needs every item, each justified in one line in the PR: (1) the BCL or a present dependency cannot
  do it; (2) maintained: a release in the last year, issues answered; (3) license MIT, Apache-2.0 or BSD; (4) wide adoption; (5) if infrastructure, it
  sits behind a port (DES-03). Versions live in `Directory.Packages.props`.
- **Why:** each dependency is attack surface, upgrade work and a lock-in risk.
- **Enforced by:** review; central package management + `packages.lock.json`.
- **Good:** "Microsoft.Extensions.Telemetry.Abstractions: BCL has no metric source generator; Microsoft, MIT; behind IMeterFactory". **Bad:** a
  package added for one string helper.
- **Status:** in force.

### CRIT-04 · The request does only what the client waits for
- **Rule:** an HTTP request does only what its answer needs, within the module's SLO latency budget. Slow work, third-party calls the client does not
  wait for, retryable work and scheduled work run in the Worker, triggered by an event (outbox) or a job. When the client must follow that work,
  answer `202 Accepted` with `Location`. Never an in-process queue or fire-and-forget task in the Api for work that must happen.
- **Why:** a crash or deploy loses in-memory work; slow calls eat the latency budget and the connection pool.
- **Enforced by:** review (SLO burn-rate alerts catch the latency side).
- **Good:** mail leaves through the outbox and is sent by Notification in the Worker. **Bad:** a `Task.Run` in an endpoint that calls a vendor after
  answering; Identity's in-memory `PendingLoginResendQueue`/`PendingLoginResendService` (a crash or redeploy drops the resend).
- **Status:** in force.

### CRIT-05 · Read another module through its query port; copy its data only with a reason
- **Rule:** default to the owner's `I<Module>Queries` port (ARCH-06). Keep a local read-only copy fed by its integration events only when one holds,
  and name it in the plan: (1) a hot path the port cannot serve within budget, (2) the read must survive the owner's outage, (3) SQL joins with own
  data. The copy's configuration states its consistency window, its source event and who keeps it in sync. Projections of the module's own data are
  updated by an `IDomainEventHandler` in the same transaction; none exists until a query justifies it.
- **Why:** each copy is a second source of truth to keep in sync; the port has none.
- **Enforced by:** review.
- **Good:** a payroll feature asks `IGeoQueries.NextBusinessDaysAsync` on each run. **Bad:** a copy of Geo's holidays "to save a call" with no stated
  window.
- **Status:** in force.

### CRIT-06 · Ask with a query port, react with an event; the announcer may ask the reactor read-only questions
- **Rule:** when a module needs an answer to continue its own work now, it calls a query port. When it must do something because another module's
  state changed, it consumes that module's integration event; the owner never calls the reactor to make it act. One exception, a user decision (D11,
  "Permitir solo lectura"): the module that announces an event may ask the module that reacts to it read-only questions through the reactor's
  `I<Module>Queries`, in one direction only (Businesses asks Access; Access never asks Businesses, and its only reference to Businesses is the event's
  Contracts). The event carries the fact, ids and the changed data a reasonable consumer needs (see `events`).
- **Why:** the owner stays unaware of who reacts; a reaction never blocks the owner's commit.
- **Enforced by:** review; `LayeringTests.Access_reaches_Businesses_only_through_its_event_contract_and_no_other_module_reaches_past_Access_contracts`
  blocks the reverse reference for the Businesses/Access pair (a generic ARCH-05 allow-list test is still planned).
- **Good:** Identity raises `VerificationCodeIssued`; Notification mails it; Businesses raises `BusinessCreated`, Access grants ownership and
  Businesses reads `IAccessQueries`. **Bad:** Identity calling a Notification service after registering; Access asking Businesses anything.
- **Status:** in force.

### CRIT-07 · Cache only read-heavy, rarely changing data, with a stated budget
- **Rule:** caching is opt-in per read port, as a `Cached*` decorator. Only read-heavy, rarely changing data qualifies. The decorator's XML doc states
  its freshness budget and its invalidation (integration event, plus a local after-commit eviction only when the writer must read its change at once).
  Never cache security state (sessions, permissions, credentials) nor data the user just wrote and expects to see. HTTP caching: see `caching`.
- **Why:** a cache is a bet on staleness; unbudgeted caches serve wrong answers silently.
- **Enforced by:** review (critic-performance).
- **Good:** `CachedCountryCatalog` over reference data evicted by `CatalogChanged`. **Bad:** caching Identity sessions; caching `GetHolidays`, whose
  overrides must show at once.
- **Status:** in force.

### CRIT-08 · Ephemeral state goes to Valkey behind its own port, never to the cache
- **Rule:** the cache holds only losable copies of data that lives elsewhere. Ephemeral state (markers, counters, limits, short sessions) lives in
  Valkey with atomic operations and a TTL, behind its own Application port with a `Valkey*` adapter.
- **Why:** fail-safe, eviction and stale reads are fine for a copy and wrong for state.
- **Enforced by:** review.
- **Good:** `ValkeyRegistrationQuota` (atomic window counter). **Bad:** `CachedAddressSessions` keeping address-session markers in FusionCache.
- **Status:** in force.
