# Craft (CRAFT)

- CRAFT-01 · The build fails on every warning
- CRAFT-02 · Style preferences are warnings
- CRAFT-03 · File-scoped namespaces match folders; usings sit outside, System first
- CRAFT-04 · Fields are `_camelCase`, private constants PascalCase, no `this.`
- CRAFT-05 · Methods stay within the complexity budget
- CRAFT-06 · One type per file, named after it, across the whole repo
- CRAFT-07 · Classes are sealed by default and internal unless they are a contract
- CRAFT-08 · Every type has an XML summary; members only when the name is not enough
- CRAFT-09 · Every `cref` resolves
- CRAFT-10 · Suppressions are local and justified
- CRAFT-11 · Write modern C#
- CRAFT-12 · One clock: injected `TimeProvider`
- CRAFT-13 · Guard clauses, one abstraction level per method, BCL before hand-rolled code
- CRAFT-14 · Formatting, spelling and Markdown are checked in CI
- CRAFT-15 · Analyzers are build-only, centrally versioned, restored locked
- CRAFT-16 · Banned APIs live in `eng/BannedSymbols.txt`, each citing its rule
- CRAFT-17 · Roslynator is installed with every rule off unless a card enables it

### CRAFT-01 · The build fails on every warning
- **Rule:** Keep `TreatWarningsAsErrors`, `AnalysisLevel` `latest-recommended`, `EnforceCodeStyleInBuild`, `Nullable` and `Deterministic` on in
  `Directory.Build.props`. Never lower them per project.
- **Why:** a warning nobody must fix is a warning nobody reads.
- **Enforced by:** compiler, `Directory.Build.props`.
- **Good:** fix the warning or suppress it per CRAFT-10 **Bad:** `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` in a csproj.
- **Status:** in force.

### CRAFT-02 · Style preferences are warnings
- **Rule:** Every `.editorconfig` code-style preference the code follows (`var`, primary constructors, pattern matching, collection expressions, and
  the like) is set to `:warning`, so the build fails; `dotnet format` fixes them. A preference the team does not want is set to `none`, never left at
  `suggestion`.
- **Why:** a suggestion is invisible in CI, so the codebase drifts into two styles.
- **Enforced by:** `EnforceCodeStyleInBuild` + `.editorconfig`; `dotnet format --verify-no-changes` in CI.
- **Good:** `csharp_style_prefer_primary_constructors = true:warning` **Bad:** `csharp_style_prefer_primary_constructors = true:suggestion`.
- **Status:** in force.

### CRAFT-03 · File-scoped namespaces match folders; usings sit outside, System first
- **Rule:** Use file-scoped namespaces equal to `RootNamespace` + folder path; `using` directives outside the namespace, `System*` first.
- **Why:** the namespace tells where the file lives; one layout across files.
- **Enforced by:** IDE0161, IDE0065, IDE0130.
- **Good:** `namespace Frappe.Messaging.JetStream;` in `src/Platform/Frappe.Messaging/JetStream/` **Bad:** `namespace Frappe.Messaging { ... }` in a
  `JetStream/` file.
- **Status:** in force.

### CRAFT-04 · Fields are `_camelCase`, private constants PascalCase, no `this.`
- **Rule:** Private instance fields are `_camelCase`; private `const` and `static readonly` fields are PascalCase; never qualify with `this.`.
- **Why:** the prefix marks state at a glance without `this.` noise.
- **Enforced by:** IDE1006 naming rules, IDE0003.
- **Good:** `private readonly Counter<long> _syncRuns;` **Bad:** `private readonly Counter<long> syncRuns;` used as `this.syncRuns`.
- **Status:** in force.

### CRAFT-05 · Methods stay within the complexity budget
- **Rule:** Cognitive complexity ≤ 15 per method and cyclomatic complexity ≤ 10. Split by intent, never by suppressing.
- **Why:** past the budget, reviewers stop following every branch.
- **Enforced by:** S3776, CA1502 (`eng/CodeMetricsConfig.txt`).
- **Good:** extract `ConfiguredList(...)` from a registration method **Bad:** `[SuppressMessage("Sonar", "S3776")]` on a handler.
- **Status:** in force.

### CRAFT-06 · One type per file, named after it, across the whole repo
- **Rule:** Each file declares one top-level type and is named after it, in every project under `src/` and `tests/`. Nested private types stay inside
  their owner.
- **Why:** a type is found by its file name; diffs stay small.
- **Enforced by:** SA1402, SA1649 (today only on `src/Modules`, `tests/Modules`, EndToEnd; widening to all of `src/` and `tests/` planned).
- **Good:** `ICommand.cs`, `IQuery.cs`, `ICommandHandler.cs` **Bad:** `Frappe.UseCases/Messaging.cs` holding `ICommand<T>`, `IQuery<T>` and both
  handler interfaces.
- **Status:** in force.

### CRAFT-07 · Classes are sealed by default and internal unless they are a contract
- **Rule:** Seal every class that is not designed for inheritance, exceptions included. Make a type `internal` unless another assembly must use it.
- **Why:** sealed types can change without breaking unknown subclasses, and the JIT devirtualizes their calls.
- **Enforced by:** CA1852 (internal) and Meziantou MA0053 (public, exceptions included), both at warning in `eng/analyzers.globalconfig`.
- **Good:** `public sealed class CountRegisteredOwner(...)` **Bad:** `public class AddressProviderUnavailableException : Exception`.
- **Status:** in force.

### CRAFT-08 · Every type has an XML summary; members only when the name is not enough
- **Rule:** Every type has a `<summary>` saying why it exists and the rule it keeps. Document a member only when its name does not say everything
  (units, ranges, failure modes, side effects). Never restate the name.
- **Why:** the why of a type is not in its code; a summary that repeats the name is noise.
- **Enforced by:** `TypeDocumentationTests.Every_type_has_a_summary` in `tests/Frappe.Architecture.Tests` (CS1591 stays off for members).
- **Good:** `/// <summary>Counts a registration once the commit that carried OwnerRegistered succeeded: ...</summary>` **Bad:** `/// <summary>The sync
  metrics.</summary>`, or no summary on `LoggingMailerTests`.
- **Status:** in force.

### CRAFT-09 · Every `cref` resolves
- **Rule:** `GenerateDocumentationFile` stays on so every `<see cref>` is compiled; a broken reference fails the build.
- **Why:** docs that point to renamed members lie.
- **Enforced by:** CS1574 (warnings as errors).
- **Good:** `<see cref="ReadingLifetime"/>` **Bad:** `<c>ReadingLifetime</c>` to dodge the check.
- **Status:** in force.

### CRAFT-10 · Suppressions are local and justified
- **Rule:** Suppress one diagnostic at the narrowest scope with a reason; when it breaks a convention, the reason starts with the rule id. A
  folder-wide relaxation lives in `.editorconfig` with a comment.
- **Why:** an unexplained suppression cannot be reviewed or removed later.
- **Enforced by:** review.
- **Good:** `[SuppressMessage("Design", "CA1031", Justification = "Exception to OBS-10: metrics never fail the business path.")]` **Bad:** `#pragma
  warning disable` at the top of a file with no reason.
- **Status:** in force.

### CRAFT-11 · Write modern C#
- **Rule:** Prefer records for values, primary constructors for injected dependencies, `required`/`init` for creation-time data, collection
  expressions and pattern matching. Language-version specifics live in `csharp.md`.
- **Why:** less ceremony, intent visible in the shape of the code.
- **Enforced by:** IDE style rules at warning (CRAFT-02); review.
- **Good:** `public sealed class CountRegisteredOwner(IdentityMetrics metrics)` **Bad:** a constructor that only copies arguments into fields.
- **Status:** in force.

### CRAFT-12 · One clock: injected `TimeProvider`
- **Rule:** Read time only from the injected `TimeProvider`; NodaTime code gets its `IClock` through `TimeProviderClock`. Never `DateTime.Now`,
  `DateTime.UtcNow`, `DateTimeOffset.UtcNow` or `SystemClock.Instance`.
- **Why:** one source of time that tests can move (TEST-11).
- **Enforced by:** BannedApiAnalyzers: `NodaTime.SystemClock.Instance` in `eng/BannedSymbols.txt` (CRAFT-12); `DesignTimeFactoryTests`.
- **Good:** `public SyncMetrics(GeoMetrics metrics, IServiceScopeFactory scopes, IClock clock)` **Bad:** `SystemClock.Instance.GetCurrentInstant()`.
- **Status:** in force.

### CRAFT-13 · Guard clauses, one abstraction level per method, BCL before hand-rolled code
- **Rule:** Return or throw early instead of nesting; keep each method at one level of abstraction; use the BCL or an existing dependency before
  writing a helper.
- **Why:** flat code reads top to bottom; hand-rolled copies of the BCL carry their own bugs.
- **Enforced by:** review (critic-craft); Roslynator rules when a card enables them (CRAFT-17).
- **Good:** `ArgumentNullException.ThrowIfNull(meters);` **Bad:** `if (meters != null) { ... } else { throw ... }`.
- **Status:** in force.

### CRAFT-14 · Formatting, spelling and Markdown are checked in CI
- **Rule:** Code passes `dotnet format --verify-no-changes`, every text passes `typos` (`eng/typos.toml`), Markdown passes markdownlint
  (`eng/markdownlint-cli2.jsonc`). Tool configs live under `eng/`, never at the root.
- **Why:** mechanical review belongs to tools, and the root stays clean.
- **Enforced by:** `code-quality.yml`.
- **Good:** a new linter config at `eng/<tool>.yaml` **Bad:** `.spectral.yaml` at the repo root.
- **Status:** in force.

### CRAFT-15 · Analyzers are build-only, centrally versioned, restored locked
- **Rule:** Reference analyzers with `PrivateAssets="all"`, versions only in `Directory.Packages.props`, and keep `packages.lock.json` with locked
  restore in CI.
- **Why:** analyzers never ship, versions never drift between projects, restores are reproducible.
- **Enforced by:** NuGet central package management; `RestoreLockedMode` in CI.
- **Good:** `<PackageReference Include="SonarAnalyzer.CSharp" PrivateAssets="all" ... />` **Bad:** `Version="..."` on a `PackageReference`.
- **Status:** in force.

### CRAFT-16 · Banned APIs live in `eng/BannedSymbols.txt`, each citing its rule
- **Rule:** Ban an API through Microsoft.CodeAnalysis.BannedApiAnalyzers with an entry in `eng/BannedSymbols.txt` whose message ends with the rule id
  that bans it in parentheses, e.g. `…(CRAFT-16)`.
- **Why:** the compiler stops the banned call and tells the writer which rule to read.
- **Enforced by:** RS0030 at warning in `eng/analyzers.globalconfig`.
- **Good:** `T:Moq.Mock`1;TEST-08: use a hand-written fake` **Bad:** a ban only in a review checklist.
- **Status:** in force.

### CRAFT-17 · Roslynator is installed with every rule off unless a card enables it
- **Rule:** Roslynator is referenced like StyleCop: every rule `none` in `.editorconfig`; a rule is turned on only by a card that names it.
- **Why:** it adds checks without fighting Sonar, StyleCop or `.editorconfig`.
- **Enforced by:** `eng/analyzers.globalconfig` (`roslynator_analyzers.enabled_by_default = false`).
- **Good:** `dotnet_diagnostic.RCS1xxx.severity = warning` next to a comment naming the card **Bad:** enabling Roslynator's default rule set.
- **Status:** in force.
