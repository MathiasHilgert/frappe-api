# Design rules

Mandatory for any task that creates or changes a type. Intent: always think decoupled, by layers and by responsibilities, swappable, using software
patterns, and search the web for one that fits when unsure.

- DES-01 · Place every type in exactly one layer
- DES-02 · Give every type one responsibility
- DES-03 · Put every external dependency behind a port so it can be swapped
- DES-04 · Choose a configured adapter at composition and fail at startup
- DES-05 · Add cross-cutting behavior as a decorator or pipeline step
- DES-06 · Apply a pattern only for its symptom, and say so
- DES-07 · When unsure, search the web for the fitting pattern and cite it
- DES-08 · Answer the coupling checklist for every new type
- DES-09 · No abstraction without a second implementation or a test need
- DES-10 · Prefer composition over inheritance

## DES-01 · Place every type in exactly one layer

- **Rule:** before writing a type, decide its layer: business rule → Domain; orchestration of a use case or a port → Application; technology →
  Infrastructure; transport → Http; published shape → Contracts. A type that needs two layers is two types.
- **Why:** a type that mixes layers drags its dependencies into the inner layers (ARCH-01, ARCH-02).
- **Enforced by:** `LayeringTests`; review.
- **Good:** `Email` (Domain) validates; `EfPrincipals` (Infrastructure) stores; `RegisterOwnerHandler` (Application) orchestrates. **Bad:** a handler
  that builds SQL, or an endpoint that applies a business rule.
- **Status:** in force.

## DES-02 · Give every type one responsibility

- **Rule:** a type has one reason to change, stated in its XML summary; when the summary needs "and", split it. Handlers orchestrate; rules live in
  aggregates, value objects or policy objects.
- **Why:** a change touches one type; tests target one behavior.
- **Enforced by:** type-level XML doc rule (planned, see `craft`) + Sonar S3776; review.
- **Good:** `CachedRegionCatalog` only caches; `EfRegionCatalog` only reads. **Bad:** a catalog adapter that reads, caches and evicts on events.
- **Status:** in force.

## DES-03 · Put every external dependency behind a port so it can be swapped

- **Rule:** the database, cache, Valkey state, mail, maps, payment, storage, third-party HTTP, message broker and clock are reached only through a
  port owned by Application (or a platform abstraction such as `TimeProvider`); the adapter lives in Infrastructure or a plug-in. Swapping a vendor
  changes one adapter and one registration, nothing else. Vendor types never appear in a port's signature.
- **Why:** vendors, prices and environments change; use cases and tests must not.
- **Enforced by:** `LayeringTests.Inner_layers_know_nothing_about_infrastructure`; BannedApiAnalyzers for `DateTime.Now`/`UtcNow` (planned, see
  `csharp`); review.
- **Good:** `IMailer` with a vendor adapter and a logging adapter; `IAddressProvider` with Google Places. **Bad:** a handler calling `HttpClient`
  against a vendor URL, or reading `DateTime.UtcNow`.
- **Status:** in force.

## DES-04 · Choose a configured adapter at composition and fail at startup

- **Rule:** when configuration picks the adapter, a selection class decides once at composition (or a keyed registration) and validates its options at
  startup, naming the missing key; use cases never branch on the environment.
- **Why:** a wrong environment fails on deploy, not on the first request.
- **Enforced by:** options validation tests; review.
- **Good:** `MailerSelection` (fails with `MissingKey`). **Bad:** `if (env.IsDevelopment()) logMailer.Send(...)` in a handler.
- **Status:** in force.

## DES-05 · Add cross-cutting behavior as a decorator or pipeline step

- **Rule:** logging, metrics, transactions, caching and retries wrap the port or handler (decorator, pipeline, resilience pipeline); the wrapped type
  does not know.
- **Why:** each concern is written once and can be removed by unregistering it.
- **Enforced by:** review.
- **Good:** `InstrumentedQueryHandler`, `TransactionalCommandHandler`, `CachedEffectiveCalendars`. **Bad:** a handler opening a transaction or timing
  itself.
- **Status:** in force.

## DES-06 · Apply a pattern only for its symptom, and say so

- **Rule:** propose a pattern when the code shows its symptom, even if the repo never used it. Name the symptom, the change it makes cheap and its
  cost in the plan or finding. Use the catalog below; this repo's examples first.
- **Why:** patterns pay only against a real change; ceremony without a symptom is cost.
- **Enforced by:** review (critic-architecture, critic-craft cite DES-06).
- **Good:** "a region `switch` grows per region → Strategy per region package". **Bad:** "added a factory for flexibility".
- **Status:** in force.

| Symptom in the code | Pattern | In this repo / in .NET |
|---|---|---|
| We may switch vendor (mail, maps, payments, storage); a vendor SDK leaks into use cases | **Port + Adapter** (hexagonal) | `IMailer` with a vendor adapter and a logging adapter; `IAddressProvider` (Google). Application owns the port, Infrastructure the adapters |
| The adapter is chosen by configuration or environment | **Factory / selection at composition** | `MailerSelection`; keyed services (`AddKeyedScoped`, `[FromKeyedServices]`) |
| A `switch`/`if` chain on a type, kind, region or plan picks behavior, and grows with each case | **Strategy** (one class per case behind an interface, resolved by key) | Region packages in Orders; keyed services or `IEnumerable<IStrategy>` with a `Supports(...)` |
| A closed, known set of variants with data | **Closed hierarchy / union + exhaustive switch** | `public closed record Error` (see `csharp`) |
| The same cross-cutting step around many operations | **Decorator / pipeline** | `InstrumentedQueryHandler`, `TransactionalCommandHandler`, `CachedEffectiveCalendars`; Polly pipelines |
| Another module's or vendor's model would leak into ours | **Anti-corruption layer / Translator** | `IIntegrationEventTranslator`; a Contracts query port with its own records (ARCH-06) |
| Business rules combine (eligibility, pricing, permissions) and are tested one by one | **Specification / Policy** objects | Small classes with `IsSatisfiedBy`, composed; or a policy port the use case asks |
| An entity's allowed actions depend on its state | **State machine in the aggregate** (guarded transitions, `Result` on invalid ones) | `Principal.ActivateOwner/SuspendOwner` returning `RuleViolation` |
| A computation runs in steps that vary independently | **Pipeline of steps** (or template method) | Prefer a list of steps over inheritance (DES-10) |
| A fact must reach other modules reliably | **Domain event → Outbox → Integration event** | `OutboxInterceptor`, relay, inbox consumers |
| A module needs another's data on a hot path or during its outage | **Local projection / read model** fed by events | Only under CRIT-05 |
| Creating a valid object needs rules | **Value object with a factory** returning `Result` | `Email.Create`, `OwnerName.Create` |
| Expensive or remote reads repeated | **Cache-aside behind the port** with event eviction | `Cached*` FusionCache decorators in Orders (CRIT-07) |
| Retries, timeouts, circuit breaking to a remote | **Resilience pipeline** (`Microsoft.Extensions.Resilience`) | Resend mailer pipeline |

## DES-07 · When unsure, search the web for the fitting pattern and cite it

- **Rule:** when no catalog row fits or the idiomatic .NET 11 form is unclear, search primary sources (Microsoft Learn, the library's docs, well-known
  authors) and put the URL in the plan, finding or PR. Never invent a pattern name or claim an idiom without a source.
- **Why:** decisions stay verifiable and current with the platform.
- **Enforced by:** review (a finding or plan that names an uncataloged pattern without a URL is incomplete).
- **Good:** "Strongly typed metrics, per <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-strongly-typed>". **Bad:** "the standard way is X"
  with no source.
- **Status:** in force.

## DES-08 · Answer the coupling checklist for every new type

- **Rule:** before adding a type, answer each question; a "yes" needs a fix or a stated reason in the plan:
  1. Does it depend on a concrete class where a port would let us replace or test it?
  2. Would changing a vendor, a region rule or a storage touch more than one adapter?
  3. Does it know another module's tables, internals or vendor types?
  4. Would adding the next case mean editing a growing conditional instead of adding a class?
  5. Could another module not reuse it without dragging in this module's internals?
- **Why:** coupling is cheapest to remove before the type has callers.
- **Enforced by:** review (critic-architecture).
- **Good:** a new region rule is a new class in its region package. **Bad:** a new `case "UY":` in Orders's core.
- **Status:** in force.

## DES-09 · No abstraction without a second implementation or a test need

- **Rule:** an internal type with one implementation and no test that needs a fake gets no interface. External dependencies (DES-03) and module ports
  always have one. A pattern whose ceremony outweighs the change it makes cheap is a finding against it.
- **Why:** each interface is a jump a reader pays for.
- **Enforced by:** review.
- **Good:** `IPrincipals` (storage port, faked in unit tests). **Bad:** `IRegionViewMapper` with one implementation used by one handler.
- **Status:** in force.

## DES-10 · Prefer composition over inheritance

- **Rule:** share behavior by composing small types (decorators, steps, strategies); inherit only from platform bases meant for it (`AggregateRoot`,
  closed hierarchies). Classes are sealed by default (see `craft`).
- **Why:** a base class couples every subclass to its changes.
- **Enforced by:** CA1852 / MA0053 (sealed, planned); review.
- **Good:** `CachedRegionCatalog(EfRegionCatalog regions, IFusionCache cache) : IRegionCatalog`. **Bad:** `CachedRegionCatalog :
  EfRegionCatalog` overriding reads.
- **Status:** in force.
