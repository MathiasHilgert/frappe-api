# Conventions

How code in this repository is shaped, and which test enforces each rule.

## Dependency rules

- Domain references nothing framework-specific; Application does not reference Infrastructure. The one concession
  to EF Core is a private parameterless constructor on entities and value objects it materializes: neutral C#, no
  attribute or EF comment, so Domain still compiles and reads without any ORM.
- Other modules may consume only a module's Contracts.
- The host knows modules only through `IModule` (its name, services, messaging, endpoints and meters).
- A module's Contracts hold messages (records), enums and constants, plus cross-module query interfaces in
  `Queries/` (see [Messages between modules](#messages-between-modules)). A service two modules share (such as
  sealing a code a message carries, `IMessageSeal`) is a platform abstraction in `Frappe.Application.Abstractions`
  with its implementation in a `Platform.*` project, never an interface in one module's Contracts.

Enforcement:

- `tests/Frappe.ArchitectureTests` checks the csproj reference graph and the compiled types. Projects are
  classified by name, so a new module is covered with no test changes.
- `eng/BannedSymbols.txt` fails the build in `src/` on `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`
  and `Guid.NewGuid`.

## Rules at a glance

| Topic | Rule |
| --- | --- |
| Validation | Domain invariants return `Result<T>` built from a `ValidationNotification` (Fowler): all broken rules at once. A failure propagates every error (`Result.Failure(other.Errors)`), never just the first. The HTTP layer checks only request shape, 400: request records carry the client's fields as sent (nullable) and the command passes them on, so the domain reports missing values with the rest. Endpoints answer failures with `result.ToProblem()` (see [Errors and localization](#errors-and-localization)). |
| Commands and queries | CQRS lite, see [Commands and queries](#commands-and-queries). |
| Edge vs internal commands | A command an endpoint sends carries the client's raw, nullable input as sent (`RegisterOwner(string? Name, string? Email, string? Password, ...)`): the domain validates it and reports every broken rule at once. A command one handler sends another, or a job publishes, carries input already validated (`IssuePasswordResetCode(Guid ResetId, string Email)`), non-nullable, and never re-validates it. |
| Events / outbox | Aggregates raise domain events; Wolverine scrapes them from EF Core into the outbox in the same transaction. Reactions run from durable local queues. |
| Realtime | A handler returns `RealtimePush.From(notification)` as a cascading message (Wolverine routes only concrete types). It is pushed to NATS JetStream after commit and streamed over SSE; the stream sequence is the SSE id, so `Last-Event-ID` resumes. |
| Jobs | One-off work is a (scheduled) Wolverine message. Recurring work is a Quartz trigger running `PublishCommandJob<T>`, which only publishes a command; a module declares it with `services.AddRecurringCommand<T>(name, cron)`. A restart keeps a stored trigger untouched unless its schedule changed, deletes `PublishCommandJob` jobs no module declares any more, and a missed firing runs once on recovery. Quartz uses its clustered PostgreSQL store (schema `quartz`), so with several instances exactly one fires each trigger. |
| Persistence | One schema per module, EF migrations history in that schema. Outside Development the app creates nothing at startup (see [Deploy](deploy.md)). Pools and roles: [database.md](database.md). |
| Security | Deny by default: every endpoint requires an authenticated user. An endpoint opts in one of two ways (`EndpointConventions`): `AllowAnonymousAuth()` (anonymous, per-IP rate limited in process, 429 documented) or `RequirePolicy(policy)` (a module wraps it, e.g. Identity's `RequireOwner()`; 401/403 documented). |
| Time and ids | Handlers inject `TimeProvider` and pass `DateTimeOffset now` into the domain, which never asks a clock; ids are UUIDv7 via `Guid.CreateVersion7(now)`. |
| Options | One pattern: defaults on the options class, `public const string SectionName`, `AddOptions<T>().BindConfiguration(T.SectionName)`, a source-generated `[OptionsValidator]` partial class (DataAnnotations, `IValidatableObject` for cross-field rules) and `ValidateOnStart()`. |
| Correlation | Every request gets an id, echoed in `X-Request-Id` and flowed into logs and spans. |
| Modules | `IModule.Name` is also the route segment: endpoints map under `/v1/{Name}`. |

## Commands and queries

Every use case is a message with one Wolverine handler, sent from the endpoint with `IMessageBus`, so every use case
gets the same tracing, and every command the same EF Core transaction and outbox.

Handler code is generated ahead of time into `src/Host/Frappe.Api/Internal/Generated` and committed. After adding
or changing a handler (or its dependencies), run `just codegen` before you push and commit the result. While you
work you do not need it: Debug builds (`just dev`, the IDE) leave the committed code out and Development generates
handlers in memory at startup (`TypeLoadMode.Dynamic`). Release builds, production and the integration tests load
only the committed code (`TypeLoadMode.Static`), and CI (`just codegen-check`) fails when it is stale.

| | Command | Query |
| --- | --- | --- |
| Message | `record` implementing `ICommand` (answers `Result`) or `ICommand<T>` (answers `Result<T>`), imperative name (`ChangeLocale : ICommand<Locale>`) | `record` implementing `IQuery<T>` (`GetMe : IQuery<OwnerProfile>`) |
| Handler | Loads the aggregate through the repository, saves, returns exactly what its marker declares (first in a tuple when it also cascades), an id or a small value; every failure, rate limits included, is an `Error` | Asks a read port (`IOwnerReads`) for a DTO and returns `Result<T>`, like a command, so callers unwrap one shape; marked `[NonTransactional]` |
| Data access | Repository, change tracking, `SaveChangesAsync` | Read port in Infrastructure: `AsNoTracking` projections straight to DTOs |
| Side effects | Cascading messages (`OutgoingMessages`) or `IMessageContext.PublishAsync`, written to the outbox in the same transaction | None |

Layout: Application holds one folder per use case (message, handler and result types together, e.g.
`Sessions/LogIn/` with `LogIn.cs` and `LogInHandler.cs`), while the namespace stays the feature area
(`...Application.Sessions`) so a type never shares a name with its namespace. A module that only reacts to other
modules' events (Notification) groups its reactions by area instead: `OwnerCodeMails/` holds one handler class per
message it reacts to (`VerificationCodeMailHandler`, ...), and the ports and exceptions they share sit in their own
feature folder (`Mails/`). Endpoints live in `<Module>.Infrastructure/Endpoints/`, one `*Endpoints` route group per
feature area with one file per request/response record (`*Request`/`*Response`, unique names). Every endpoint is
mapped by its full route on the module group (`group.MapPost("/password-resets/{id:guid}/confirmation", ConfirmAsync)`,
no nested `MapGroup`) to a named `private static` method, never an inline lambda, so a route reads whole on one line
and every body has the same shape; platform endpoints (`/v1/stream`) follow the same style. They return a typed
`Results<...>` with `WithName`, `WithSummary` and `ProducesProblem` metadata, answer a success body that is an
Infrastructure `*Response` record (never an Application DTO), read the caller with
`ICurrentPrincipal.RequiredSession()`, the client address with `HttpContext.ClientIp()`, and only map HTTP to a
message and a result to a status or `ToProblem()`. JSON is camelCase everywhere (the framework default). Use-case policy, attempt
limits included, lives in the handler: a throttled command fails with a `TooManyRequests` error carrying
`Error.WithRetryAfter(wait)`, and facts the edge may disclose ride in `Error.WithMetadata(key, value)`. Transport
concerns (cookies through `ISessionCookie`, `Content-Language`) stay in Infrastructure.
A command no caller awaits, whether cascaded by another handler (`IssuePasswordResetCode`) or published by a job
(`PurgePendingOwners`), implements `IBackgroundCommand`: nobody reads its answer and a published handler's return
value is cascaded, so it returns only its `OutgoingMessages`.
`Error` keeps default record equality. To ask whether an error breaks a given rule, compare by `Code` and `Field` with
`error.Is(IdentityErrors.EmailTaken)` or the `Error.ByRule` comparer (e.g. `ShouldContain(x, Error.ByRule)`): that
holds for a throttled or annotated copy too, where `==` does not.
`tests/Frappe.ArchitectureTests/CqrsConventionTests.cs` fails when an endpoint calls an Application service, a message
is not a marked record, a query handler touches a repository, a command handler answers with a type some query
returns, a handler answers anything but what its marker declares (`ICommand` → `Result`, `ICommand<T>` and
`IQuery<T>` → `Result<T>`), a background command handler returns a `Result`, or a query handler is not `[NonTransactional]`. `EdgeConventionTests.cs` fails when an endpoint builds a
problem itself (`TypedResults.Problem`, `Results.Problem`, `new ProblemDetails`) or answers a bare status
(`TypedResults.Unauthorized`, `NotFound`, `BadRequest`, `Forbid`, ...), reads `HttpContext.User` or
`Connection.RemoteIpAddress`, returns a success body that is not an Infrastructure `*Response`, lives outside
`Endpoints/` or is not named `*Endpoints`, or an `Error` is declared outside a `*Errors` catalogue.
`VisibilityTests.cs` fails when a module's Infrastructure exposes a public type other than its `IModule` and the
adapters marked `[InlinedByWolverine]` (Wolverine constructs those inline in the generated handlers only when public).
`ModuleConventionTests.cs` fails when the host references a module type other than its `IModule`, a Contracts project
holds anything but messages, enums and constants (plus query interfaces in `Queries/`), a file's namespace is not its project plus feature folder, an
Application use-case folder lacks `<UseCase>.cs` and `<UseCase>Handler.cs`, or a module has more than one
`ModuleDbContext`. `DomainPurityTests.cs` fails when a domain method takes a `TimeProvider` or a domain property has a
non-private setter. `OptionsConventionTests.cs` fails when an options class has no `[OptionsValidator]` or names its
section anything but `SectionName`. In the integration suite, `EndpointConventionTests.cs` reads the route table:
every module endpoint sits under `/v1/{IModule.Name}`, has a name, a summary and its problems documented, and is
either `AllowAnonymousAuth()` (rate limited) or authorized with its 401/403; `ModuleSchemaTests.cs` checks every module
table lives in the module's own schema and maps no other module's type; `StepUp_GuardsExactlyTheEndpointsThatChangeHowTheOwnerSignsIn` pins the step-up policy. `ContractsConventionTests.cs` requires a `[MessageIdentity]` on every Contracts message and forbids cycles between modules through Contracts.

Not checked by a test (reviewed by hand): that a handler loads only the aggregate it changes, that domain events are
named in the past tense, that a use case's policy lives in its handler rather than the endpoint, and that translated
strings read naturally in each locale.

Migrations:

```sh
just migration Identity <Name>   # add a migration to a module's Persistence/Migrations
just migrate                     # apply every module's migrations locally (Development)
dotnet ef migrations bundle -p … -s src/Host/Frappe.Api -c IdentityDbContext   # run before the app starts
```

## Health

Any module declares what it needs to be ready, next to where it registers the client:

```csharp
services.AddHealthChecks()
    .AddDependency("postgres", DependencyCriticality.Critical, async (sp, ct) => { /* ping */ });
```

- **Critical** down: `/health/ready` returns 503 and the instance leaves rotation (Postgres).
- **NonCritical** down: `/health/ready` stays 200 with status `Degraded` (NATS, Valkey).
  Valkey is still configuration-required: outside Development a missing `ConnectionStrings__valkey` fails startup
  (checked when the app starts, so `resources setup` does not need it).
- `/health/live` never checks dependencies, so a database outage does not restart every instance.
- Each check times out after 2 s. Results are also exported as the `frappe.health.dependency.status` gauge.

## Errors and localization

Every response says which language it speaks: `Content-Language` is always set and `Vary: Accept-Language` appended.
Platform locales are `es-AR`, `en`, `es-MX`, `pt-BR` (default `en`; override with `Localization:SupportedLocales`
and `Localization:DefaultLocale`). The request locale (`IRequestLocale`) is negotiated by RFC 4647 lookup:

1. Each `Accept-Language` tag in quality order: exact tag, its truncations, then the first configured locale of the
   same language (`es-CL` → `es-AR`). The first servable tag wins, even over a stored preference.
2. Otherwise the principal's stored preference, read through `IPreferredLocaleSource` (the owning module implements it).
3. Otherwise `en`.

Failures are RFC 9457 problems. `result.ToProblem()` picks the status from `Error.Kind` (422 validation, 404, 409,
429 or 503 with `Retry-After` from the longest `Error.RetryAfter`, 401, 403; mixed kinds answer 422), sets `type` to
`urn:frappe:problem:<code>` (one error) or `urn:frappe:problem:validation` (several), and translates `title` and each
`errors[].message` by error code. A code with no catalogue entry falls back to `Error.Message`, in English. One error
also sets `detail` (same text) and `code`, and every `Error.Metadata` entry becomes a top-level member (e.g. `ownerId`
on a pending owner's login; the keys live in the module's Contracts, `IdentityErrorMetadata`). Endpoints never shape
a problem themselves: give the error the right `Kind` instead. A 401/403 from authorization (challenge or forbid)
answers the same shape with `platform.auth.required` or `platform.auth.forbidden` (`PlatformErrors`), the
in-process rate limiter with `platform.rate_limited`, and an `UnavailableException` (e.g. no password-hashing slot
freed in time) with a 503 carrying its error.

Two layers slow abusive clients down, and neither ever locks an account. The attempt limiter (Valkey, fail-open)
counts failures per use case: a sign-in has a tight budget per email and client IP together (10 per 15 minutes, so an
attacker hammering an address from their own IP only throttles themselves), a much higher ceiling per email alone
(100, reached only by a distributed attack) and one per IP. An address the email already signed in from in the last
30 days is exempt from that ceiling, so a distributed attack cannot lock the owner out of the places they use (the
server-side form of OWASP's device-cookie defence); it still has its own tight budget. Underneath, an in-process
ASP.NET rate limiter caps every anonymous auth endpoint per IP (`Platform:RateLimiting`), so the API stays protected
when Valkey is down. Every per-IP key comes from `ClientIp()`: an IPv4 address as is, an IPv6 address by its /64
prefix. One IPv6 /64 is one budget: ISPs hand each subscriber at least a whole /64 (RFC 6177), so keying by full
address would let one client rotate through billions of addresses and dodge every per-IP limit. The cost is the same
as a shared IPv4 NAT: devices behind one /64 share its budget. An origin is remembered by a command the sign-in
cascades, so only after the session committed. Throttled sign-ins, rejected codes, known-origin lookups skipped
during a Valkey outage, purge outcomes (purged, skipped on a conflict, failed) and dead-lettered mails are metered
(`Frappe.Identity`,
`Frappe.Notification`, `Wolverine:*` meters).

A module adds strings as flat ICU MessageFormat catalogues, one file per locale, keyed by error code or a
module-prefixed key:

```xml
<EmbeddedResource Include="i18n/*.json" />   <!-- i18n/en.json, i18n/es-AR.json, i18n/es-MX.json, i18n/pt-BR.json -->
```

```json
{ "identity.email.taken": "That email is already registered.",
  "identity.codes.left": "{count, plural, one {# attempt left} other {# attempts left}}" }
```

Register them with `services.AddMessageCatalog(typeof(SomeModuleType).Assembly)` and format with
`IMessageCatalog.Format(locale, key, args)`. A key missing in a locale falls back to another region of the same
language, then to `en`; a key missing in `en` throws. In the module's tests:

```csharp
MessageCatalogConformance.Check(assembly).ShouldBeEmpty();                 // every en key in every locale, every message formats
MessageCatalogConformance.CodesWithoutEntries(assembly, codes).ShouldBeEmpty(); // every error code translated
```


## Namespaces

Namespace = project + feature folder, not the full folder path: `Owners/RegisterOwner/RegisterOwner.cs` is in
`Frappe.Identity.Application.Owners`. A use-case folder groups files without adding a namespace segment, so a type
never shares its name with its namespace. IDE0130 (namespace must match folder) is therefore off in `.editorconfig`;
`ModuleConventionTests.SourceFile_NamespaceIsTheProjectPlusItsFeatureFolder` enforces the rule instead.

## Metrics

Copy `NotificationMetrics` (one instrument) or `IdentityMetrics` (several) when a module needs a meter:

- One `internal sealed class <Module>Metrics` in Infrastructure, registered as a singleton, listed in
  `IModule.Meters` through its `public const string MeterName = "Frappe.<Module>"`.
- One `public const string <What>Name` per instrument, shaped `frappe.<module>.<thing>.<event>` in snake case
  (`frappe.identity.login.throttled`). Tests read the constant, never a copied string.
- Application code depends on a port (`IIdentityTelemetry`) with intention-revealing methods (`LoginThrottled()`);
  the metrics class implements it. Counters for failures that fail open, so an outage shows on a dashboard.

## Messages between modules

- **Wire identity.** Every message in a module's Contracts carries `[MessageIdentity("<module>.<name>.v<n>")]`
  (`identity.owner_verification_code_issued.v1`). Envelopes persist that name, not the CLR type, so a rename or
  move never strands a message in flight. A breaking change to a message's shape is a new message with the next
  version, consumed side by side until the old one drains. Introducing identities changed the persisted names once:
  drain the outbox and queues before deploying that release.
- **Personal data.** An event carries ids plus only what its consumer cannot fetch. `OwnerVerificationCodeIssued`
  carries the principal id and the sealed code; the mailer reads name, address and locale through
  `IOwnerDirectory`. A secret it must carry (a one-time code) is sealed with `IMessageSeal`. The reset and
  email-change events still carry the address, because it is not fetchable from the consumer's side at send time
  (an email change goes to the new, not yet confirmed, address).
- **Synchronous queries.** A module that must read another module's data now asks through an interface in the
  owner's `Contracts/Queries/` folder (`Frappe.Identity.Contracts.Queries.IOwnerDirectory`), with the records it
  answers in the same folder. The owner implements it in Infrastructure against its own schema and registers it;
  the call runs in process, in the caller's scope. Use it for reads only; a change in another module is a message.
  `ContractsConventionTests` forbids cycles in the module graph built from Contracts references.

## Step-up policy

A fresh step-up (`RequireStepUp()`, the password re-entered within five minutes) guards what changes how the owner
signs in or where account mail goes: requesting an email change. Changing the password re-verifies the current
password inline, which is the same proof. Revoking a session and signing out need no step-up: they only reduce
access, and an owner who suspects a stolen session must be able to cut it off at once. An attacker holding a stolen
session can sign the owner's other devices out, but cannot change the password or the email without the password.
`EndpointConventionTests.StepUp_GuardsExactlyTheEndpointsThatChangeHowTheOwnerSignsIn` pins the list.

## Sign-in throttling details

- The right password on a pending or suspended account clears that email and IP's failure count (whoever holds the
  password gains nothing from more guesses) but opens no session and does not remember the origin.
  `LoginTests.TheRightPassword_OnASuspendedAccount_ClearsThatIpsFailures_ButRemembersNoOrigin` pins it.
- A known origin is the client IP alone (an IPv6 /64), with no device cookie: everyone on one shared Wi-Fi or NAT
  shares it, so an attacker there also skips the per-email ceiling, while still facing the per email+IP and per-IP
  budgets. Accepted for needing no client support; see `ValkeyKnownSignInOrigins.KeyFor`.

## Realtime access

`GET /v1/stream?businessId=` answers 403 unless `IBusinessMembership` says the caller belongs to that business. The
platform registers a deny-all implementation until a business module replaces it, so the stream is closed by
default.
