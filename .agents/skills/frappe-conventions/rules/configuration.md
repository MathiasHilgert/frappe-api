# Configuration (CFG)

- CFG-01 · Every setting binds to an options type validated at startup
- CFG-02 · An options type names its section and defaults to production values
- CFG-03 · Every options type has a sealed `IValidateOptions<T>`
- CFG-04 · Every setting is documented in its property's XML doc
- CFG-05 · A configured list replaces the default list whole
- CFG-06 · Jobs are scheduled only through `AddCronJob`
- CFG-07 · The Worker is composed in one place and tested as Production
- CFG-08 · Outbound HTTP has explicit resilience budgets
- CFG-09 · Dependent budgets are validated to fit each other
- CFG-10 · Secrets come only from the environment
- CFG-11 · Each process connects with its own least-privilege role
- CFG-12 · No preview features

## CFG-01 · Every setting binds to an options type validated at startup

- **Rule:** Read configuration only through an options type registered with `BindConfiguration(Section)` and `ValidateOnStart()`. Every failure
  message reads `Configuration '<Section>:<Key>' must ...`.
- **Why:** a wrong value fails the deploy at start, naming the key, instead of failing the first request that reads it.
- **Enforced by:** `WorkerCompositionTests`; review.
- **Good:** `AddOptions<JetStreamOptions>().BindConfiguration(JetStreamOptions.Section)...ValidateOnStart();` **Bad:**
  `configuration["Messaging:Url"]` read in a service.
- **Status:** in force.

## CFG-02 · An options type names its section and defaults to production values

- **Rule:** Each options type exposes `public const string Section` (and `ConnectionStringName` when it reads one). Property initializers hold the
  production values; environments override only what differs.
- **Why:** an empty production config must be a safe production config; the key is never retyped as a string.
- **Enforced by:** review.
- **Good:** `public const string Section = "Billing:Login"; public int FailuresPerClient { get; set; } = DefaultFailuresPerClient;` **Bad:**
  `.BindConfiguration("Billing:Login")` with defaults only in `appsettings.json`.
- **Status:** in force.

## CFG-03 · Every options type is validated by data annotations and a generated validator

- **Rule:** Annotate the options with data annotations whose message names the full configuration key, and declare
  `[OptionsValidator] public sealed partial class XValidator : IValidateOptions<X>`. Nested options use `[ValidateObjectMembers]` or
  `[ValidateEnumeratedItems]`; cross-field rules use `IValidatableObject`. The generator ignores `ErrorMessage` on `[Range]`: use a small
  `ValidationAttribute` (such as `PortAttribute`) when the message must name the key. Rules that depend on composition (role passwords
  keyed by the database participants, consumer steps declared in DI) and named options whose configuration section is chosen at registration (such as
  per-policy rate limits) stay a hand-written sealed `IValidateOptions<T>` that collects every
  failure. No inline `.Validate(lambda, message)`.
- **Why:** one declarative style, every failure reported at once, and a validator a unit test can call, without hand-written range checks.
- **Enforced by:** BannedApiAnalyzers on `OptionsBuilder<T>.Validate` overloads; DataAnnotations stay banned in Domain and Application projects
  (DOM-09); review.
- **Good:** `[Required(ErrorMessage = "Configuration 'ConnectionStrings:Valkey' is required.")]` with
  `[OptionsValidator] public sealed partial class CachingOptionsValidator : IValidateOptions<CachingOptions>;` **Bad:** `.Validate(o => o.Port > 0,
  "...")`.
- **Status:** in force.

## CFG-04 · Every setting is documented in its property's XML doc

- **Rule:** Each options property's `<summary>` states its full key (`Section` + name), what it controls, its default, its valid range and the effect
  of changing it.
- **Why:** the code is the only settings reference; operators read it from the type, not from a separate document.
- **Enforced by:** review.
- **Good:** `/// <summary><c>Billing:Login:FailuresPerClient</c>: failed logins one client may make against one address per window.</summary>` plus
  the default and range **Bad:** `/// <summary>The NATS server URL.</summary>`.
- **Status:** in force.

## CFG-05 · A configured list replaces the default list whole

- **Rule:** When configuration sets a list, it replaces the default; never merge by index.
- **Why:** .NET binding merges arrays by index, so a shorter configured list silently keeps the default's tail.
- **Enforced by:** review.
- **Good:** `options.RetryBackoff = ConfiguredList(configuration, section, nameof(OutboxRelayOptions.RetryBackoff)) ?? options.RetryBackoff` **Bad:**
  binding `RetryBackoff` over an initialized array.
- **Status:** in force.

## CFG-06 · Jobs are scheduled only through `AddCronJob`

- **Rule:** Schedule a job with `quartz.AddCronJob<TJob>(new JobKey(name, module), "<Module>:<Job>", defaultCron)`; its cron is overridable and
  validated. Jobs are `[DisallowConcurrentExecution]` and run on the clustered durable store. A module job purges its outbox/inbox through a
  broker-free message-purge port in `Frappe.Messaging.Abstractions` (planned), or the Worker host wires it to `MessagePurger`.
- **Why:** one schedule per job across replicas, overridable per environment, checked at start.
- **Enforced by:** `WorkerCompositionTests`.
- **Good:** `quartz.AddCronJob<OrdersMessagePurgeJob>(new JobKey("purge-messages", "orders"), "Orders:MessagePurge", "0 30 3 * * ?")` **Bad:**
  `quartz.ScheduleJob<T>(t => t.WithCronSchedule("0 30 3 * * ?"))`.
- **Status:** in force.

## CFG-07 · The Worker is composed in one place and tested as Production

- **Rule:** `WorkerComposition.AddFrappeWorker` composes the Worker; each module contributes `Add<Module>Messaging`/`Add<Module>Jobs`. A test builds
  it in the Production environment.
- **Why:** a missing registration or invalid default fails CI, not the deploy.
- **Enforced by:** `WorkerCompositionTests`.
- **Good:** `.AddOrdersMessaging()` inside `AddFrappeWorker` **Bad:** registrations in `Program.cs` of the Worker.
- **Status:** in force.

## CFG-08 · Outbound HTTP has explicit resilience budgets

- **Rule:** Every outbound client uses `AddStandardResilienceHandler` with explicit attempt timeout, retries and total timeout. A retried call with
  side effects sends an idempotency key. Call `RemoveAllLoggers()` when URLs carry user data.
- **Why:** default budgets outlive the caller's timeout; retries without a key duplicate effects; logged URLs leak typed text.
- **Enforced by:** review.
- **Good:** `.AddStandardResilienceHandler(o => { o.AttemptTimeout.Timeout = AttemptTimeout; o.Retry.MaxRetryAttempts = Retries; })` **Bad:**
  `services.AddHttpClient<T>()` with defaults.
- **Status:** in force.

## CFG-09 · Dependent budgets are validated to fit each other

- **Rule:** When one timeout bounds another, the validator enforces the ordering with its margin (handler duration ≥ mail total + 20 s; command
  timeout > role `statement_timeout`).
- **Why:** an outer budget shorter than the inner one cancels legitimate work halfway.
- **Enforced by:** `WorkerCompositionTests`; the module's validator.
- **Good:** `MinHandlerDuration = MailingModule.TotalTimeout + TimeSpan.FromSeconds(20)` checked at start **Bad:** two independent defaults that
  happen to fit today.
- **Status:** in force.

## CFG-10 · Secrets come only from the environment

- **Rule:** Secrets come from environment variables (`deploy/.env`, required with `${VAR:?...}` in compose). Never commit, log or default them; an
  empty value means unset. A committed placeholder is the base64 of text starting with `local-only-`, unpadded (CI appends to values ending in
  `=`), and allow-listed in `.gitleaks.toml`.
- **Why:** a committed or logged secret is leaked for good.
- **Enforced by:** compose `${VAR:?}`; `.env.example` review.
- **Good:** `POSTGRES_PASSWORD: ${POSTGRES_OWNER_PASSWORD:?set it in .env}` **Bad:** `"ApiKey": "re_123"` in `appsettings.json`.
- **Status:** in force.

## CFG-11 · Each process connects with its own least-privilege role

- **Rule:** Each process and module uses a Postgres role granted only what that process does; only the Migrator runs DDL.
- **Why:** a compromised or buggy Worker cannot alter schemas or touch data it does not own.
- **Enforced by:** `WorkerCompositionTests`; migration grants.
- **Good:** Worker's `ConnectionStrings:Billing` names `frappe_billing_relay` **Bad:** every process using the owner role.
- **Status:** in force.

## CFG-12 · No preview features

- **Rule:** Same as CS-09 (canonical card in `csharp.md`), including its one approved exception, Runtime Async.
- **Why:** one rule, one place.
- **Enforced by:** see CS-09.
- **Good / Bad:** see CS-09.
- **Status:** in force.
