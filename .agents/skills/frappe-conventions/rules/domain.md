# Domain rules (DOM)

- DOM-01 · Business failures are `Result` values, never exceptions
- DOM-02 · Errors use the closed `Error` kinds
- DOM-03 · Error codes are `<module>.<subject>.<problem>` and never change
- DOM-04 · Error parameters are client-safe primitives without PII
- DOM-05 · A value object is a sealed record built by `Create` → `Result<T>`
- DOM-06 · Types holding PII or secrets mask `ToString`
- DOM-07 · Aggregates are sealed, live only in Domain and guard their own state
- DOM-08 · State changes raise past-tense domain events
- DOM-09 · Validate every input together and report every failure
- DOM-10 · Validation lives in handlers and value objects only
- DOM-11 · Every module uses the same model, Mailing included

## DOM-01 · Business failures are `Result` values, never exceptions

- **Rule:** A use case or aggregate that can fail for a business reason returns `Result<T>` (`Result<Unit>` for transitions). Throw only for bugs and
  infrastructure faults; argument guards (`ArgumentNullException.ThrowIfNull`) are bugs and may throw. A handler that expects an exception from a port
  (a lost race, a refused provider) turns it into a `Result` at that call.
- **Why:** Exceptions as control flow hide the failure paths from the signature, cost a stack trace per rejection and get logged as incidents.
- **Enforced by:** review.
- **Good:** `return Error.RuleViolation(BillingErrorCodes.OwnerNotActive);` (`Principal.ResetOwnerPassword`). **Bad:** `throw new
  OwnerNotActiveException();` from a transition.
- **Status:** in force.

## DOM-02 · Errors use the closed `Error` kinds

- **Rule:** Build errors with the factories of `Frappe.Results.Error` (`InvalidValue`, `RuleViolation`, `NotFound`, `Conflict`, `Forbidden`, …). Never
  derive a new kind in a module; a new kind is added to `Frappe.Results` and handled wherever the kinds are switched on.
- **Why:** `Error` is a `closed record`: every switch over its kinds (HTTP status, problem type) is exhaustive, so a new kind is a compile error where
  it is not handled yet.
- **Enforced by:** compiler (closed hierarchy: derivation outside `Frappe.Results` does not compile).
- **Good:** `Error.InvalidValue(BillingErrorCodes.EmailInvalid)`. **Bad:** `record PaymentDeclined(...) : Error` in a module.
- **Status:** in force.

## DOM-03 · Error codes are `<module>.<subject>.<problem>` and never change

- **Rule:** Codes are lower case, dot separated, prefixed by the module (`orders.shipment_year.out_of_range`). Code constants live in Domain in
  `<Module>ErrorCodes`; `Error` instances that Application builds live in Application in `<Module>Errors`. A published code is never renamed or
  reused.
- **Why:** Codes are public contract: clients branch on them and message catalogs are keyed by them.
- **Enforced by:** review; ArchTest (planned): codes declared only in `<Module>ErrorCodes` and prefixed by the module.
- **Good:** `BillingErrorCodes.OwnerNotActive` in `Frappe.Billing.Domain`, `OrdersErrors.RegionNotFound(...)` in `Frappe.Orders.Application`. **Bad:**
  `Error.NotFound("not_found")` inline in a handler.
- **Status:** in force.

## DOM-04 · Error parameters are client-safe primitives without PII

- **Rule:** Parameters are strings, numbers, booleans, dates or arrays of those, and never personal data, secrets or entities.
- **Why:** Parameters go to API clients as they are and errors get logged.
- **Enforced by:** review.
- **Good:** `Error.InvalidValue(BillingErrorCodes.PasswordTooLong, ("max", MaxLength))`. **Bad:** `Error.InvalidValue(code, ("email", raw))`.
- **Status:** in force.

## DOM-05 · A value object is a sealed record built by `Create` → `Result<T>`

- **Rule:** A value object is a `sealed record` (a `sealed class` when record equality is wrong) with a private constructor and `public static
  Result<T> Create(string? raw)` that normalizes, then validates. Persistence rebuilds it through a separate trusted factory (`FromStored`), never
  through `Create`. A factory of a closed union of recipient-style values built only from code-authored data (never external input) may throw
  `ArgumentException` instead; `Create` returning `Result<T>` stays for external input.
- **Why:** An invalid value cannot exist, and the one place that builds it is the one place that validates it.
- **Enforced by:** review.
- **Good:** `public sealed record Email { private Email(string value) => Value = value; public static Result<Email> Create(string? raw) ... }`.
  **Bad:** `public MailAddress(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); ... }`.
- **Status:** in force.

## DOM-06 · Types holding PII or secrets mask `ToString`

- **Rule:** Every value object, command, event or view that holds personal data or a secret overrides `ToString` to print only what is safe (a masked
  form, ids, timestamps).
- **Why:** Records print every member by default; one interpolation into a log or exception message leaks the data.
- **Enforced by:** FRP001 (`eng/analyzers/Frappe.Analyzers`) at warning in `eng/analyzers.globalconfig` for records with a secret- or
  email-named public string member (SEC-07); review for the rest.
- **Good:** `public override string ToString() => "***" + Value[Value.IndexOf('@', StringComparison.Ordinal)..];` (`Email`), `=> "***"` (`Password`).
  **Bad:** a `sealed record RegisterOwner(string? Email, string? Password)` with the generated `ToString`.
- **Status:** in force.

## DOM-07 · Aggregates are sealed, live only in Domain and guard their own state

- **Rule:** An aggregate is a `sealed class : AggregateRoot` in `*.Domain`, with a private parameterless constructor for EF, static factories, private
  setters and transitions returning `Result<Unit>` that `Raise` their events. Only Domain types derive from `AggregateRoot`; data without behavior
  (catalog rows, projections) is a plain `*Row`. Every aggregate carries the `xmin` row version (UOW-04).
- **Why:** Invariants hold only if nothing outside the aggregate can set its state; an Infrastructure row posing as an aggregate drags domain events
  and unit-of-work dispatch into persistence code.
- **Enforced by:** `LayeringTests.Only_sealed_types_in_a_domain_project_derive_from_AggregateRoot` (planned).
- **Good:** `public sealed class Principal : AggregateRoot { private Principal() { ... } public static Principal RegisterOwner(...) ... }`. **Bad:**
  `internal sealed class RegionRow : AggregateRoot` in Infrastructure; `public Guid Id { get; set; }`.
- **Announcements:** an event announces a change of the aggregate that raises it; a change spanning several rows of one concept (the Orders catalog:
  regions and currencies) gets its own aggregate (a one-row root with a `Revision`) that raises it, never a sibling aggregate speaking for others.
- **Status:** in force.

## DOM-08 · State changes raise past-tense domain events

- **Rule:** A transition that changes state calls `Raise(new <Fact>(...))` with a past-tense name and the data a handler needs. In-transaction
  reactions are `IDomainEventHandler<T>`; losable reactions after commit are `IAfterCommitDomainEventHandler<T>` (UOW-09). Domain never publishes
  integration events; the module's translator in Infrastructure maps them.
- **Why:** Side effects stay out of the aggregate and the outbox gets the fact in the same save.
- **Enforced by:** review; LayeringTests `Inner_layers_know_nothing_about_infrastructure` (no messaging in Domain).
- **Good:** `Raise(new OwnerEmailChanged(Id, old, newEmail, Owner.Name, Locale, now, keptSessionId));`. **Bad:** a handler that calls the mailer after
  `ChangeOwnerEmail` instead of reacting to the event.
- **Status:** in force.

## DOM-09 · Validate every input together and report every failure

- **Rule:** When a request carries several inputs, validate all of them and return every error at once with `Result.Combine` or a `Notification`; stop
  early only when a later check depends on an earlier value.
- **Why:** A client fixing one field per round trip is a bad API.
- **Enforced by:** review.
- **Good:** `notification.Add(Error.InvalidValue(...)); ... return notification.HasErrors ? notification.ToFailure<LocalizedText>() : new
  LocalizedText(accepted);`. **Bad:** `if (email.IsFailure) return email.Errors;` before the password was checked.
- **Status:** in force.

## DOM-10 · Validation lives in handlers and value objects only

- **Rule:** Commands and queries carry raw nullable primitives; handlers turn them into value objects through `Create` and check request-level rules.
  Never use DataAnnotations, FluentValidation or ASP.NET Core's `AddValidation()` for business input.
- **Why:** One validation path returning `Result` errors with stable codes; a second framework yields a second error shape and rules split across
  layers.
- **Enforced by:** BannedApiAnalyzers (planned): `System.ComponentModel.DataAnnotations` in Application and Domain, and `AddValidation`.
- **Good:** `public sealed record RegisterOwner(string? GivenName, string? FamilyName, string? Email, string? Password, ...)`, then
  `Email.Create(command.Email)` in the handler. **Bad:** `public sealed record RegisterOwner([Required, EmailAddress] string Email)`;
  `builder.Services.AddValidation();`.
- **Status:** in force.

## DOM-11 · Every module uses the same model, Mailing included

- **Rule:** Every module has Domain, Application, Contracts, Infrastructure and Http projects; its use cases are commands and queries returning
  `Result`, run through `AddHandlers` and its own unit of work (UOW-01). No module is exempt because it is small or has no HTTP routes yet.
- **Why:** One model means one way to read, review and instrument any module; exceptions become the places bugs hide.
- **Enforced by:** ArchTest (planned): each module ships the five projects and its Domain references `Frappe.Results`; `UnitOfWorkPipelineTests`
  `A_command_of_a_module_without_a_unit_of_work_fails_naming_the_module_instead_of_saving_nothing`.
- **Good:** Billing and Orders: `Frappe.<M>.Application/<Feature>/<UseCase>/` with handlers, `AddUnitOfWork<TContext>(Module)`. **Bad:** Mailing:
  no Contracts or Http project, no commands, no unit of work, `MailAddress` throwing.
- **Status:** in force.
