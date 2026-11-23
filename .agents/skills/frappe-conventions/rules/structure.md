# Structure rules

Where files go and what they are called. Layer dependencies: `architecture`. Use-case shape: `cqrs`, `domain`.

- STR-01 · Every module has the same project set
- STR-02 · Application is organized by feature, then one folder per use case
- STR-03 · Infrastructure is organized by feature plus Persistence/ and Messaging/ only
- STR-04 · Plug-in packages are named `Frappe.<Module>.<Kind>.<Name>`
- STR-05 · Use cases are named for intent; handlers add `Handler`
- STR-06 · Ports are capability nouns; adapters are prefixed by technology
- STR-07 · Aggregates live only in Domain and are mapped directly
- STR-08 · `*Row` names only data without domain behavior
- STR-09 · Each EF configuration sits next to the type it maps
- STR-10 · `InternalsVisibleTo` only toward the module's own test projects
- STR-11 · Each layer exposes its host facet from one fixed class
- STR-12 · No module rewrites the composed handler graph
- STR-13 · Tests mirror the production projects

### STR-01 · Every module has the same project set
- **Rule:** `src/Modules/<M>/` holds `Frappe.<M>.Domain`, `.Application`, `.Contracts`, `.Infrastructure`, `.Http`, plus plug-in packages (STR-04).
  Every module, Notification included; no module invents a different layout.
- **Why:** one shape to navigate; ArchTests and tools target every module the same way (same model for all: see `domain`, `cqrs`).
- **Enforced by:** module project-set ArchTest over `Frappe.slnx` (planned).
- **Good:** `src/Modules/<M>/` with its five projects. **Bad:** a module with no Contracts or Http project.
- **Status:** in force.

### STR-02 · Application is organized by feature, then one folder per use case
- **Rule:** `Application/<Feature>/<UseCase>/` holds `<UseCase>.cs` (public sealed record request) and `<UseCase>Handler.cs` (internal sealed); ports,
  views and exceptions shared by the feature sit loose in `<Feature>/`; only module-wide types (`<M>Errors`, `<M>Metrics`, `<M>QueriesRegistration`, a
  type several features use) sit at the project root. A helper used by one use case lives in that use case's folder.
- **Why:** a feature is read and changed in one place; deleting a use case deletes one folder.
- **Enforced by:** review (folder-shape ArchTest planned).
- **Good:** `Geo.Application/Countries/GetCountry/{GetCountry,GetCountryHandler}.cs`, `Countries/ICountryCatalog.cs`. **Bad:** one use case's helper
  at the project root; a port shared by the feature inside one use case's folder.
- **Status:** in force.

### STR-03 · Infrastructure is organized by feature plus Persistence/ and Messaging/ only
- **Rule:** feature folders hold their adapters (`Ef*`, `Cached*`, `Valkey*`), cached records and EF configurations. Only module-wide pieces go in the
  two fixed technical folders: `Persistence/` (DbContext, design-time factory, migration runner, migrations, database options) and `Messaging/`
  (translator, consumers, relay wiring). No other technical folder.
- **Why:** a feature's storage is found next to the feature; technical folders do not become dumping grounds.
- **Enforced by:** review.
- **Good:** `Geo.Infrastructure/Countries/{CountryRow,CountryRowConfiguration,EfCountryCatalog,CachedCountryCatalog}.cs`. **Bad:**
  `<M>.Infrastructure/Configuration/<M>DatabaseOptions.cs` (belongs in `Persistence/`).
- **Status:** in force.

### STR-04 · Plug-in packages are named `Frappe.<Module>.<Kind>.<Name>`
- **Rule:** a package that plugs into a module's core is `Frappe.<Module>.<Kind>.<Name>` in `src/Modules/<Module>/<Kind>/`, with `<Kind>` plural
  (`Countries`, `Sources`, `Producers`). Dependency direction: ARCH-14.
- **Why:** the name says what the package plugs in and where its siblings live.
- **Enforced by:** `PlugInNamingTests` (tests/Frappe.Architecture.Tests); review.
- **Good:** `Frappe.Geo.Countries.Argentina`. **Bad:** `Frappe.Geo.Global` (→ `Frappe.Geo.Sources.Global`), `Frappe.Notification.Identity` (→
  `Frappe.Notification.Producers.Identity`).
- **Status:** in force.

### STR-05 · Use cases are named for intent; handlers add `Handler`
- **Rule:** a request is a verb phrase in the business language (`GetCountry`, `ListCountries`, `RegisterOwner`); its handler is `<Request>Handler`.
  Response naming: see `cqrs`.
- **Why:** the folder listing reads as the module's capabilities.
- **Enforced by:** review (handler-suffix ArchTest planned).
- **Good:** `GetCountry` → `GetCountryHandler`. **Bad:** `CountryQuery`, `CountryService.Get`.
- **Status:** in force.

### STR-06 · Ports are capability nouns; adapters are prefixed by technology
- **Rule:** an Application port names a capability (`IPrincipals`, `ICountryCatalog`, `IMailer`); its adapter is `<Technology><Capability>`: `Ef*` (EF
  Core), `Cached*` (cache decorator), `Valkey*` (Valkey state), `Hmac*`, `Resend*`, and so on.
- **Why:** the name tells which dependency an adapter wraps and what to swap.
- **Enforced by:** review.
- **Good:** `ICountryCatalog` → `EfCountryCatalog`, `CachedCountryCatalog`. **Bad:** `CountryCatalogImpl`, `CountryRepository2`.
- **Status:** in force.

### STR-07 · Aggregates live only in Domain and are mapped directly
- **Rule:** only Domain types extend `AggregateRoot`. Infrastructure maps them as they are with fluent configuration (STR-09): private constructors
  and setters, no EF attributes or EF types in Domain, no parallel persistence class for an aggregate.
- **Why:** persistence ignorance keeps one model of the business; a second persistence class drifts from it.
- **Enforced by:** ArchTest "only Domain extends AggregateRoot" (planned); `LayeringTests.Inner_layers_know_nothing_about_infrastructure` (no EF in
  Domain).
- **Good:** `Principal` mapped by `PrincipalConfiguration`. **Bad:** `internal sealed class CountryRow : AggregateRoot` in Infrastructure.
- **Status:** in force.

### STR-08 · `*Row` names only data without domain behavior
- **Rule:** `*Row` is an Infrastructure persistence type for data with no business behavior: reference catalogs, read projections, technical records.
  It never extends `AggregateRoot` and never raises domain events.
- **Why:** the suffix tells a reader there are no rules to look for.
- **Enforced by:** `LayeringTests.Only_sealed_types_in_a_domain_project_derive_from_AggregateRoot`.
- **Good:** `CountryNameRow`. **Bad:** `CountryRow`, `SubdivisionRow`, `SyncRunRow` extending `AggregateRoot`.
- **Status:** in force.

### STR-09 · Each EF configuration sits next to the type it maps
- **Rule:** one `IEntityTypeConfiguration<T>` per mapped type, named `<Type>Configuration`, in the feature folder of that type; the DbContext applies
  configurations from its assembly and holds only mapping.
- **Why:** mapping changes with its feature, not in a shared folder.
- **Enforced by:** review.
- **Good:** `Geo.Infrastructure/Countries/CountryRowConfiguration.cs`. **Bad:** `Identity.Infrastructure/Persistence/PrincipalConfiguration.cs`,
  `SessionConfiguration.cs`, `VerificationCodeConfiguration.cs`.
- **Status:** in force.

### STR-10 · `InternalsVisibleTo` only toward the module's own test projects
- **Rule:** declare it only as `<InternalsVisibleTo>` in the csproj, only toward the same module's (or platform's) test projects; never toward another
  production assembly, never as an `[assembly:]` attribute.
- **Why:** internals stay internal; a production consumer must go through a public contract.
- **Enforced by:** csproj ArchTest (planned).
- **Good:** `Frappe.Geo.Infrastructure.csproj` → `Frappe.Geo.UnitTests`, `Frappe.Geo.IntegrationTests`. **Bad:** `Frappe.Geo.Infrastructure` →
  `Frappe.Geo.Global`.
- **Status:** in force.

### STR-11 · Each layer exposes its host facet from one fixed class
- **Rule:** Infrastructure: `public sealed class <M>Module : IModule` and `<M>Database : IDatabaseModule`, with internal `<M>PersistenceRegistration`
  behind them; Application: `<M>QueriesRegistration` (the `I<M>Queries` facade); Http: `public sealed class <M>Http : IHttpModule` with route mapping;
  a plug-in: its own `IModule`. Handlers are registered by `AddHandlers(assembly)`, never one by one.
- **Why:** composition is found in one place per layer, and hosts only list the facets (ARCH-11).
- **Enforced by:** `LayeringTests`; review.
- **Good:** `OrdersModule.AddServices(builder)` → `AddOrdersPersistence(...)`. **Bad:** a host registering `EfOrderCatalog` itself.
- **Status:** in force.

### STR-12 · No module rewrites the composed handler graph
- **Rule:** Same as CQRS-04 (canonical card in `cqrs.md`); read it there.
- **Status:** see CQRS-04.

### STR-13 · Tests mirror the production projects
- **Rule:** `tests/Modules/<M>/Frappe.<M>.UnitTests` and `.IntegrationTests`; platform has `Frappe.Platform.UnitTests` and `.IntegrationTests`;
  cross-cutting suites in `tests/Frappe.Architecture.Tests` and `tests/Frappe.EndToEnd.Tests`; shared helpers in `tests/Shared/Frappe.Testing` (see
  `tests`).
- **Why:** unit and integration suites run with different dependencies; the split is checkable.
- **Enforced by:** unit-test dependency check (planned).
- **Good:** `tests/Modules/<M>/Frappe.<M>.UnitTests`. **Bad:** `tests/Platform/Frappe.Platform.Tests` mixing both.
- **Status:** in force.
