# C# 15 and modern idiom rules (CS)

- CS-01 · Closed variant sets are closed hierarchies or unions, switched without a default arm
- CS-02 · Collection expressions, with `with(...)` for constructor arguments
- CS-03 · Primary constructors for dependency-only classes
- CS-04 · Records for values and messages; `required`/`init` for bound data
- CS-05 · File-scoped namespaces
- CS-06 · Pattern matching over casts and null checks
- CS-07 · Extension members in `extension` blocks
- CS-08 · Labeled `break`/`continue` instead of flags
- CS-09 · No preview language, runtime or SDK features
- CS-10 · One clock: injected `TimeProvider`
- CS-11 · Async all the way, with the caller's `CancellationToken`
- CS-12 · No `ConfigureAwait` in app code

## CS-01 · Closed variant sets are closed hierarchies or unions, switched without a default arm

- **Rule:** A fixed set of variants that carry data is a `closed` record hierarchy or a `union`; every `switch` over it lists each case and has no
  `_`/`default` arm. Enums are only for data-less values; a switch over an enum names every member and its discard arm only throws
  `UnreachableException`.
- **Why:** With no default arm a new variant is a compile error at every switch that does not handle it yet, instead of a runtime throw.
- **Enforced by:** compiler (CS8509 non-exhaustive switch, an error under `TreatWarningsAsErrors`); review for `abstract` hierarchies that should be
  `closed` and for default arms.
- **Good:** `public closed record Error`; `result switch { Failure failure => ..., T value => ... }`. **Bad:** `public abstract record MailBlock` with
  `_ => throw new NotSupportedException(...)` in `StandardMailLayout.HtmlBlock`.
- **Status:** in force.

## CS-02 · Collection expressions, with `with(...)` for constructor arguments

- **Rule:** Create and spread collections with `[...]`; pass capacity or comparer through `[with(capacity: n), .. items]` /
  `[with(StringComparer.Ordinal)]` instead of falling back to `new`.
- **Why:** One syntax for every collection, and the compiler picks the cheapest construction.
- **Enforced by:** IDE0028, IDE0300–IDE0305 at warning in `.editorconfig`.
- **Good:** `private static readonly HashSet<int> MessageErrorCodes = [10054];`; `new AreaListResponse([.. list.Select(ToResponse)])`;
  `List<Translation> accepted = [with(capacity: count)];`. **Bad:** `var accepted = new List<Translation>(); var seen = new HashSet<LanguageTag>();`
  (`LocalizedText.Create`).
- **Status:** in force.

## CS-03 · Primary constructors for dependency-only classes

- **Rule:** A class whose constructor only receives dependencies uses a primary constructor and reads the parameters directly; no field-copying
  constructors. Keep an explicit constructor only when it validates or computes.
- **Why:** Removes boilerplate that hides what a class depends on.
- **Enforced by:** IDE0290 (`csharp_style_prefer_primary_constructors`) raised to warning (planned).
- **Good:** `internal sealed class TransactionalCommandHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IServiceProvider
  services)`. **Bad:** `private readonly IClock _clock; public X(IClock clock) { _clock = clock; }`.
- **Status:** in force.

## CS-04 · Records for values and messages; `required`/`init` for bound data

- **Rule:** Requests, events, views, responses and value objects are `sealed record`s (positional when short). Options and other types bound by a
  serializer or configuration use `required` or `init` properties, never public setters.
- **Why:** Value equality and immutability are the default for data; `required` turns a missing member into a compile error.
- **Enforced by:** review.
- **Good:** `public sealed record AddSubdivisionAlias(string SubdivisionCode, string? Alias) : ICommand<SubdivisionAliasView>;`. **Bad:** `public
  class AddSubdivisionAliasRequest { public string Alias { get; set; } }`.
- **Status:** in force.

## CS-05 · File-scoped namespaces

- **Rule:** Every file declares `namespace X;` matching its folder.
- **Why:** One indentation level less in every file, and one namespace per file.
- **Enforced by:** IDE0161 (`csharp_style_namespace_declarations = file_scoped:warning`); IDE0130.
- **Good:** `namespace Frappe.Billing.Domain.Owners;`. **Bad:** `namespace Frappe.Billing.Domain.Owners { ... }`.
- **Status:** in force.

## CS-06 · Pattern matching over casts and null checks

- **Rule:** Use `is`/`switch` patterns (type, property, relational, list, `not`, `is { } x`) instead of `as` + null checks, chained `&&` member checks
  or casts.
- **Why:** The condition reads as the shape it tests, and the binding is scoped and non-null.
- **Enforced by:** IDE0019, IDE0020, IDE0078, IDE0083 raised to warning (planned).
- **Good:** `if (Owner is not { Status: OwnerStatus.Active })`; `if (proof.Code is not { Purpose: CodePurpose.EmailChange, NewEmail: { } newEmail })`.
  **Bad:** `if (Owner == null || Owner.Status != OwnerStatus.Active)` followed by `var code = proof.Code as EmailChangeCode;`.
- **Status:** in force.

## CS-07 · Extension members in `extension` blocks

- **Rule:** Write extension methods, properties and indexers inside `extension(T receiver) { ... }` blocks in a static class named
  `<Subject>Extensions` or after the feature.
- **Why:** One receiver declaration per block, and extension properties and indexers become available.
- **Enforced by:** review.
- **Good:** `public static class ResultHttpExtensions { extension<T>(Result<T> result) { public Results<TSuccess, ErrorsProblemHttpResult>
  ToTypedHttpResult<TSuccess>(Func<T, TSuccess> onSuccess) where TSuccess : IResult => ...; } }`. **Bad:**
  `public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)`.
- **Status:** in force.

## CS-08 · Labeled `break`/`continue` instead of flags

- **Rule:** To leave or skip an outer loop from an inner one, use `break outer;`/`continue outer;`. Do not introduce a `found`/`done` flag or a `goto`
  for it, and do not label loops that need no jump.
- **Why:** The jump says where control goes; a flag spreads one decision over several lines.
- **Enforced by:** review.
- **Good:** `outer: foreach (var row in rows) { foreach (var cell in row) { if (cell.IsEmpty) continue outer; } ... }`. **Bad:** `var skip = false;
  foreach (...) { if (...) { skip = true; break; } } if (skip) continue;`.
- **Status:** in force.

## CS-09 · No preview language, runtime or SDK features

- **Rule:** Never enable `LangVersion=preview`, `EnablePreviewFeatures`, `<Features>` flags of preview runtime features, or APIs marked
  `[RequiresPreviewFeatures]`.
- **Why:** Preview features change or disappear between releases and are unsupported in production.
- **Enforced by:** CA2252; review of `Directory.Build.props` and `*.csproj`.
- **Good:** `<TargetFramework>net11.0</TargetFramework>` with the default language version. **Bad:** `<LangVersion>preview</LangVersion>`.
- **Exception (approved):** Runtime Async (`<Features>$(Features);runtime-async=on</Features>` in `Directory.Build.props`) stays on; its comment
  carries `Exception to CS-09` and `TraceFlowTests`/`WorkerHostTests` pin that context still flows. Re-evaluate when it ships stable.
- **Status:** in force (the props comment gains its exception marker in alignment).

## CS-10 · One clock: injected `TimeProvider`

- **Rule:** Read time only through an injected `TimeProvider`, or NodaTime's `IClock` bridged by `TimeProviderClock`. Never `DateTime.Now`/`UtcNow`,
  `DateTimeOffset.Now`/`UtcNow` or `SystemClock.Instance`. Timers and delays take the same `TimeProvider`.
- **Why:** One source of time that tests control with one `FakeTimeProvider`, so instants and timers move together.
- **Enforced by:** BannedApiAnalyzers (`eng/BannedSymbols.txt`); `DesignTimeFactoryTests`.
- **Good:** `public sealed class TimeProviderClock(TimeProvider time) : IClock { public Instant GetCurrentInstant() =>
  Instant.FromDateTimeOffset(time.GetUtcNow()); }`. **Bad:** `SystemClock.Instance` in `OrdersDesignTimeDbContextFactory` and
  `BillingDesignTimeDbContextFactory`.
- **Status:** in force.

## CS-11 · Async all the way, with the caller's `CancellationToken`

- **Rule:** Every method that does I/O is async, takes a `CancellationToken` as its last parameter and passes it to every call that accepts one. Never
  block on a task (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`). Use `CancellationToken.None` only on purpose, with a comment.
- **Why:** Blocking starves the thread pool; a dropped token keeps working for a client that already left.
- **Enforced by:** CA2016; Meziantou MA0040 and MA0042 (planned).
- **Good:** `// Not the caller's token: a cancelled command still has to give its changes back. await
  unitOfWork.RollbackAsync(CancellationToken.None);`. **Bad:** `var user = repo.FindAsync(id, default).Result;`.
- **Status:** in force.

## CS-12 · No `ConfigureAwait` in app code

- **Rule:** Do not write `ConfigureAwait(false)` (or `true`) in `src/` or `tests/`; CA2007 stays off.
- **Why:** ASP.NET Core and the generic host have no `SynchronizationContext`, so it changes nothing and only adds noise.
- **Enforced by:** BannedApiAnalyzers (planned): `Task.ConfigureAwait`, `Task<T>.ConfigureAwait`, `ValueTask.ConfigureAwait`.
- **Good:** `result = await inner.HandleAsync(command, cancellationToken);`. **Bad:** `await
  unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);` (`TransactionalCommandHandler`, `Instrumentation`).
- **Status:** in force.
