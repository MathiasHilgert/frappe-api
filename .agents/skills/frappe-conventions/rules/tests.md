# Tests (TEST)

- TEST-01 · TUnit on Microsoft.Testing.Platform only
- TEST-02 · Unit tests never touch a database, NATS or Valkey
- TEST-03 · tests/ mirrors src/, with unit and integration projects per module and for Platform
- TEST-04 · Test names are plain-English sentences that state the behavior
- TEST-05 · Every test marks Arrange, Act, Assert, with exactly one Act
- TEST-06 · One behavior per test; every observable effect inside `Assert.Multiple()`
- TEST-07 · Test classes are `public sealed` with a summary
- TEST-08 · Doubles are hand-written fakes; mocking libraries are banned
- TEST-09 · Test data comes from intent-named factories with fixed values
- TEST-10 · Test through the public interface
- TEST-11 · Time is faked only with `FakeTimeProvider`
- TEST-12 · Timestamps are compared as `Instant`
- TEST-13 · Waits go through the shared `WaitUntil`; no `Task.Delay` or `Thread.Sleep`
- TEST-14 · Integration tests use real services in Testcontainers fixtures
- TEST-15 · Container reuse is a local opt-in
- TEST-16 · Tests share no mutable state
- TEST-17 · Performance rules are pinned by deterministic guards
- TEST-18 · Golden files go through `Snapshot`
- TEST-19 · Adapters are tested against trimmed real vendor files
- TEST-20 · Architecture rules are NetArchTest tests
- TEST-21 · Published contracts are regenerated, and a stale one fails
- TEST-22 · Changed lines are covered and suites are bounded
- TEST-23 · Every rule a change introduces has its own test
- TEST-24 · Analyzer relaxations for tests live only in `.editorconfig`

### TEST-01 · TUnit on Microsoft.Testing.Platform only
- **Rule:** Every `*Tests` project is a TUnit executable on Microsoft.Testing.Platform; its csproj holds only references (the rest comes from
  `tests/Directory.Build.props`).
- **Why:** one runner, one CLI (`dotnet test --project`), one set of flags in CI.
- **Enforced by:** `tests/Directory.Build.props`.
- **Good:** `<ProjectReference Include="..." />` only **Bad:** a test csproj adding `xunit` or its own `OutputType`.
- **Status:** in force.

### TEST-02 · Unit tests never touch a database, NATS or Valkey
- **Rule:** A `*.UnitTests` project runs without Docker, network, database, broker or cache: it has no direct `PackageReference` to
  `Testcontainers.*`, `Npgsql*`, `NATS.*` or `StackExchange.Redis` and never opens a connection; driver types reached through project references (a
  connection-string builder) are allowed. Anything that needs one is an integration test or higher.
- **Why:** unit suites stay fast and runnable anywhere; a hidden container makes them neither.
- **Enforced by:** `UnitTestsStayInProcess` in `tests/Directory.Build.props` (direct references); review (connections).
- **Good:** `IdentityFakes` in-memory ports in `Frappe.Identity.UnitTests` **Bad:** `Testcontainers.PostgreSql` in a project that holds unit tests.
- **Status:** in force.

### TEST-03 · tests/ mirrors src/, with unit and integration projects per module and for Platform
- **Rule:** `tests/Modules/<M>/Frappe.<M>.UnitTests` and `Frappe.<M>.IntegrationTests`; `tests/Platform/Frappe.Platform.UnitTests` and
  `Frappe.Platform.IntegrationTests`; cross-cutting `Frappe.Architecture.Tests` and `Frappe.EndToEnd.Tests`; fixtures both suites of a module (or of
  Platform) use live in its `Frappe.<M>.Testing` (`tests/Platform/Frappe.Platform.Testing` for Platform), helpers any suite may use in
  `tests/Shared/Frappe.Testing`; a test project never compiles another test project's source files. Folders inside mirror the source folders.
- **Why:** the test of a type is found by its path, and each suite has one cost profile.
- **Enforced by:** review; `UnitTestsStayInProcess` in `tests/Directory.Build.props`.
- **Good:** `tests/Modules/<M>/Frappe.<M>.UnitTests/<Feature>/<Type>Tests.cs` **Bad:** `tests/Platform/Frappe.Platform.Tests` mixing fakes and
  Testcontainers.
- **Status:** in force.

### TEST-04 · Test names are plain-English sentences that state the behavior
- **Rule:** Name a test as a sentence with underscores that states the behavior the reader can rely on: no `Should`, no method name, no
  `Method_Scenario_Expected`. Names pass `typos`.
- **Why:** behavior names survive refactors and read as the specification; method-bound names break when the code moves.
- **Enforced by:** review; `typos` in CI.
- **Good:** `A_network_failure_is_retryable` **Bad:** `SendAsync_Throws_ShouldReturnRetryable`.
- **Status:** in force.

### TEST-05 · Every test marks Arrange, Act, Assert, with exactly one Act
- **Rule:** Mark the phases with `// Arrange`, `// Act`, `// Assert`; exactly one Act per test. Omit `// Arrange` only when there is nothing to
  prepare.
- **Why:** the reader sees at once what is set up, what is exercised and what is checked; two Acts are two tests.
- **Enforced by:** review.
- **Good:** `// Act` above the single `await mailer.SendAsync(...)` **Bad:** a blank-line-only layout, or a second `SendAsync` after the first
  assertions.
- **Status:** in force.

### TEST-06 · One behavior per test; every observable effect inside `Assert.Multiple()`
- **Rule:** A test proves one rule. Check every observable effect of that rule (result, persisted data, published event, metric, log) inside one
  `using (Assert.Multiple())` block.
- **Why:** one failure reports every broken effect at once, and a test name stays true.
- **Enforced by:** review.
- **Good:** `using (Assert.Multiple()) { await Assert.That(result.IsSuccess).IsTrue(); await Assert.That(fakes.Sessions.Added).HasSingleItem(); }`
  **Bad:** asserting the result and forgetting the stored row.
- **Status:** in force.

### TEST-07 · Test classes are `public sealed` with a summary
- **Rule:** Test classes are `public sealed` and carry a `<summary>` saying what they test and against what (fakes, a container, a deployed image).
- **Why:** the summary tells which suite cost and which collaborator a failure points to.
- **Enforced by:** `TypeDocumentationTests` (src and tests); CA1852/MA0053.
- **Good:** `/// <summary>The Resend adapter against a fake handler: ... No network.</summary> public sealed class ResendMailerTests` **Bad:** `public
  sealed class LoggingMailerTests` with no summary.
- **Status:** in force.

### TEST-08 · Doubles are hand-written fakes; mocking libraries are banned
- **Rule:** Fake ports in memory, one fakes object per module (`<Module>Fakes`); assert on the fake's state, not on calls. HTTP goes through
  `RecordingHandler`. Moq and NSubstitute are banned.
- **Why:** state-based fakes survive refactors and behave like the real port; interaction mocks pin the implementation.
- **Enforced by:** BannedApiAnalyzers (`eng/BannedSymbols.txt`, planned).
- **Good:** `fakes.Sessions.Added` **Bad:** `sessions.Verify(s => s.Add(It.IsAny<Session>()), Times.Once)`.
- **Status:** in force.

### TEST-09 · Test data comes from intent-named factories with fixed values
- **Rule:** Build data with factories on the module's fakes object, named for intent, with valid defaults and parameters only for what the test
  highlights. Values are fixed and readable; nothing random. Random strings are allowed only for isolation keys (database, role, stream namespace),
  never for data a test asserts on.
- **Why:** a reader sees what matters in the call; a failure reproduces the same way every run.
- **Enforced by:** review.
- **Good:** `fakes.PendingOwner(email: "martina@example.com")` **Bad:** `var id = Guid.NewGuid();` as the message id a test asserts on.
- **Status:** in force. The end-to-end mails use Guid.NewGuid only as an isolation key (one address per run against a shared stack), never as an
  asserted value.

### TEST-10 · Test through the public interface
- **Rule:** Exercise the handler, endpoint, adapter or consumer a caller uses; never a private member or an internal step. Assert every observable
  effect of the rule.
- **Why:** tests that reach inside break on refactors that keep behavior.
- **Enforced by:** review (critic-tests).
- **Good:** `await handler.HandleAsync(command, ct)` then check the fakes **Bad:** reflection into a private method.
- **Status:** in force.

### TEST-11 · Time is faked only with `FakeTimeProvider`
- **Rule:** Fake time with `Microsoft.Extensions.Time.Testing.FakeTimeProvider`; the NodaTime `IClock` comes from it through `TimeProviderClock`. No
  hand-written clock classes, no wall clock.
- **Why:** one fake moves both clocks together; copies of `FixedClock` drift apart.
- **Enforced by:** BannedApiAnalyzers on `DateTime.Now`, `DateTime.UtcNow`, `SystemClock.Instance` in tests (planned); review.
- **Good:** `var time = new FakeTimeProvider(start); var clock = new TimeProviderClock(time);` **Bad:** `private sealed class FixedClock(Instant now)
  : IClock`.
- **Status:** in force.

### TEST-12 · Timestamps are compared as `Instant`
- **Rule:** Compare timestamps as NodaTime `Instant`, never as formatted strings or `DateTime` with a kind.
- **Why:** Postgres rounds to microseconds and `DateTime` kinds compare wrong across zones.
- **Enforced by:** review.
- **Good:** `await Assert.That(row.CreatedAt).IsEqualTo(IdentityFakes.Now);` **Bad:** `IsEqualTo(now.ToString("O"))`.
- **Status:** in force.

### TEST-13 · Waits go through the shared `WaitUntil`; no `Task.Delay` or `Thread.Sleep`
- **Rule:** Poll an eventual effect only through `WaitUntil` in `Frappe.Testing` (timeout plus a failure message saying what never happened).
  `Task.Delay` and `Thread.Sleep` are banned in tests except inside it; a handler that must hang awaits a `TaskCompletionSource` with the token.
- **Why:** hand-rolled loops either flake or wait too long, and fail with no message.
- **Enforced by:** BannedApiAnalyzers in tests (planned).
- **Good:** `await WaitUntil.TrueAsync(() => mailpit.HasMailTo(email), "the registration mail never arrived");` **Bad:** `await
  Task.Delay(TimeSpan.FromMilliseconds(500));` in a loop.
- **Status:** in force.

### TEST-14 · Integration tests use real services in Testcontainers fixtures
- **Rule:** Postgres, Valkey and NATS run in Testcontainers as TUnit fixtures shared with `ClassDataSource` (`PerTestSession`, `PerClass` or `None`).
  No EF in-memory provider, no SQLite.
- **Why:** constraints, SQL, locks and broker semantics only behave like production in the real service.
- **Enforced by:** review.
- **Good:** `[ClassDataSource<InboxDatabase, NatsServer>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]` **Bad:**
  `UseInMemoryDatabase("test")`.
- **Status:** in force.

### TEST-15 · Container reuse is a local opt-in
- **Rule:** Call `WithReuse(true)` only when `FRAPPE_TESTCONTAINERS_REUSE=true`; never in CI. Each test isolates its own data so a reused container
  stays correct.
- **Why:** reuse speeds local loops; in CI it hides state leaking between runs.
- **Enforced by:** review.
- **Good:** `.WithReuse(Environment.GetEnvironmentVariable("FRAPPE_TESTCONTAINERS_REUSE") == "true")` **Bad:** `.WithReuse(true)`.
- **Status:** in force.

### TEST-16 · Tests share no mutable state
- **Rule:** Tests share no mutable static state. Tests that must touch a process-wide resource use `[NotInParallel(key)]`; NATS and Valkey tests
  isolate by namespace or key prefix.
- **Why:** TUnit runs tests in parallel; shared state makes failures depend on order.
- **Enforced by:** review.
- **Good:** `[NotInParallel(nameof(HandlerPipelineTests))]` **Bad:** a `static List<string>` filled by several tests.
- **Status:** in force.

### TEST-17 · Performance rules are pinned by deterministic guards
- **Rule:** Pin a performance rule by counting commands (`EfCommandLog`) or reading the plan (`EXPLAIN` with `enable_seqscan = off`), never by timing.
- **Why:** timings flake on shared runners; a command count or plan does not.
- **Enforced by:** the guard tests themselves.
- **Good:** `var commands = new EfCommandLog();` then assert one command **Bad:** `Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(50)`.
- **Status:** in force.

### TEST-18 · Golden files go through `Snapshot`
- **Rule:** Compare large outputs (mail bodies, generated contracts) with `Snapshot.MatchesAsync`; refresh with `FRAPPE_UPDATE_SNAPSHOTS=true` and
  review the diff.
- **Why:** the diff of the golden file is the review of the change.
- **Enforced by:** `Snapshot` (fails on mismatch).
- **Good:** `await Snapshot.MatchesAsync(mail.Content.Html, $"Identity/registration_attempted.{locale}.html");` **Bad:** `Contains("Hola")` on a whole
  HTML mail.
- **Status:** in force.

### TEST-19 · Adapters are tested against trimmed real vendor files
- **Rule:** An adapter that parses a vendor format is tested against a real response, trimmed to the cases that matter, under the test project's
  `Fixtures/`.
- **Why:** invented samples miss the vendor's quirks.
- **Enforced by:** review.
- **Good:** `Fixtures/places/details.json` cut from a real Google Places answer **Bad:** a JSON literal typed from the vendor's docs.
- **Status:** in force.

### TEST-20 · Architecture rules are NetArchTest tests
- **Rule:** A rule about references, layers or naming that code can check is a NetArchTest test in `Frappe.Architecture.Tests`.
- **Why:** a layering rule only in prose erodes one PR at a time.
- **Enforced by:** `LayeringTests` and siblings.
- **Good:** `Types.InAssembly(domain).ShouldNot().HaveDependencyOnAny(applicationName, "Frappe.UseCases")` **Bad:** a layering rule kept only in
  review.
- **Status:** in force.

### TEST-21 · Published contracts are regenerated, and a stale one fails
- **Rule:** Regenerate published contracts with `just contracts update`; a committed contract that differs from the generated one fails a test, and
  oasdiff gates breaking changes.
- **Why:** consumers read the committed file; it must match the code.
- **Enforced by:** `OpenApiContractTests`, `IntegrationEventContractTests`, oasdiff in CI.
- **Good:** `just contracts update` in the same commit as the route change **Bad:** hand-editing the contract JSON.
- **Status:** in force.

### TEST-22 · Changed lines are covered and suites are bounded
- **Rule:** At least 80% of changed lines are covered. Every suite in CI runs with `--timeout` and `--maximum-failed-tests 10`.
- **Why:** untested changes reach main unnoticed; a hung suite must fail fast instead of eating the job.
- **Enforced by:** `.github/workflows/tests.yml` coverage gate.
- **Good:** `dotnet test --project ... --timeout 3m --maximum-failed-tests 10 --coverage` **Bad:** a new suite step without a timeout.
- **Status:** in force.

### TEST-23 · Every rule a change introduces has its own test
- **Rule:** Each requirement has a test; each case the rule names has its case; changed shared code gets a test proving its previous behavior still
  holds.
- **Why:** an untested rule is a wish, and shared code breaks callers nobody was looking at.
- **Enforced by:** review (critic-tests); coverage gate.
- **Good:** one test per refusal reason of a new validation **Bad:** one happy-path test for a rule with three refusals.
- **Status:** in force.

### TEST-24 · Analyzer relaxations for tests live only in `.editorconfig`
- **Rule:** Tests relax only the rules listed under `[tests/**.cs]` in `.editorconfig` (CA1707, CA2007, CA1861, S3776, CA1502, S1075, S1313, S5332,
  S2068); anything else follows CRAFT-10.
- **Why:** one visible list instead of pragmas scattered through tests.
- **Enforced by:** `.editorconfig`; warnings as errors.
- **Good:** underscores in test names under CA1707 off **Bad:** `#pragma warning disable CA1848` at the top of a test file.
- **Status:** in force.
