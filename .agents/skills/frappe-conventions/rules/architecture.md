# Architecture rules

Modular monolith, hexagonal per module. Where a module lives and what it may reach. When to create a module, extract to platform or copy another
module's data: see `criteria`.

- ARCH-01 · Layers point inward: Domain ← Application ← Infrastructure/Http
- ARCH-02 · Domain, Application and Contracts know no infrastructure library
- ARCH-03 · Domain never knows its use cases
- ARCH-04 · Infrastructure never serves HTTP; Http reaches use cases only through Application
- ARCH-05 · A module reaches another only through its `*.Contracts`, on an allow-list
- ARCH-06 · Sync cross-module reads go through a read-only `I<Module>Queries` port
- ARCH-07 · Published contracts only grow
- ARCH-08 · Integration event names are `frappe.<module>.<fact>.v<N>` and never change
- ARCH-09 · Domain events become integration events only in Infrastructure
- ARCH-10 · One module, one schema, one data-only login role
- ARCH-11 · Composition happens only in hosts, from explicit module lists
- ARCH-12 · Modules emit no spans or exporters; business metrics only
- ARCH-13 · Application ports never save
- ARCH-14 · Plug-in packages depend on their core, never the reverse
- ARCH-15 · Namespaces follow folders
- ARCH-16 · Durable consumer names are permanent
- ARCH-17 · One type per file
- ARCH-18 · Platform holds no business rules and never knows a module

## ARCH-01 · Layers point inward: Domain ← Application ← Infrastructure/Http

- **Rule:** Domain references only `Frappe.Results`, `Frappe.Localization`, `Frappe.Aggregates` and NodaTime. Application references its Domain and
  Contracts; Infrastructure and Http reference Application; nothing references Infrastructure except hosts and its own plug-ins.
- **Why:** business rules stay testable and unchanged when storage, transport or vendors change.
- **Enforced by:** csproj references + `LayeringTests.Inner_layers_know_nothing_about_infrastructure`.
- **Good:** `Frappe.Orders.Http.csproj` → `Frappe.Orders.Application` only. **Bad:** `Frappe.X.Application` → `Frappe.X.Infrastructure` to reach an
  `Ef*`
  type.
- **Status:** in force.

## ARCH-02 · Domain, Application and Contracts know no infrastructure library

- **Rule:** inner layers never depend on EF Core, Npgsql, ASP.NET Core, NATS, Quartz, FusionCache, StackExchange.Redis, OpenTelemetry or the
  infrastructure platform libraries (`Frappe.Persistence`, `Messaging`, `Caching`, `Scheduling`, `Http`, `Observability`). A port's signature uses
  only BCL, NodaTime and module types.
- **Why:** a vendor type in a use case pins the use case to that vendor (see `design`).
- **Enforced by:** `LayeringTests.Inner_layers_know_nothing_about_infrastructure` (Orders.Contracts and Frappe.UseCases not yet in its arguments: add
  them, planned).
- **Good:** `Task<OrdersRegion?> FindRegionAsync(string regionCode, CancellationToken ct)`. **Bad:** a port returning `IQueryable<RegionRow>` or
  taking a `DbContext`.
- **Status:** in force.

## ARCH-03 · Domain never knows its use cases

- **Rule:** Domain never references its Application project nor `Frappe.UseCases`.
- **Why:** the model must not depend on how it is orchestrated.
- **Enforced by:** `LayeringTests.The_domain_does_not_depend_on_its_use_cases` (planned).
- **Good:** `Principal.ActivateOwner()` returns `Result<Unit>`. **Bad:** an aggregate taking an `ICommand` or a handler.
- **Status:** in force.

## ARCH-04 · Infrastructure never serves HTTP; Http reaches use cases only through Application

- **Rule:** `*.Infrastructure` never depends on ASP.NET Core. `*.Http` dispatches commands and queries through Application ports and never references
  Infrastructure, EF, Npgsql, `Frappe.Persistence`, `Frappe.Caching` or Redis.
- **Why:** transport and storage change independently; an endpoint that reads a table bypasses the use case's rules.
- **Enforced by:** `LayeringTests.Infrastructure_adapters_do_not_serve_http` (planned),
  `LayeringTests.The_http_adapter_reaches_use_cases_only_through_the_application_layer` (planned; the Billing-only version is gone with the
  module: write it over every `*.Http`).
- **Good:** endpoint → `IQueryHandler<GetRegion, RegionView>`. **Bad:** endpoint injecting `OrdersDbContext`.
- **Status:** in force.

## ARCH-05 · A module reaches another only through its `*.Contracts`, on an allow-list

- **Rule:** an assembly of module A may reference module B only through `Frappe.B.Contracts`, and only when the pair (A-assembly → B) is on the
  isolation allow-list. Never another module's Domain, Application, Infrastructure, Http, tables or services.
- **Why:** each module can change its internals, schema and deployment without breaking others; the allow-list makes every new dependency a reviewed
  decision.
- **Enforced by:** `LayeringTests.Only_Billings_mail_package_reaches_Billings_contracts_from_another_module` and
  `Billing_knows_nothing_about_mailing` (both planned: no Billing or Mailing module exists yet);
  one generic module-isolation ArchTest with an allow-list over every production assembly
  (planned).
- **Good:** `Frappe.Mailing.Billing` → `Frappe.Billing.Contracts` (allow-listed). **Bad:** `Frappe.Orders.Application` →
  `Frappe.Billing.Application`.
- **Status:** in force.

## ARCH-06 · Sync cross-module reads go through a read-only `I<Module>Queries` port

- **Rule:** a synchronous read of another module goes through `I<Module>Queries` in that module's Contracts: read-only, primitives and Contracts
  records in and out, implemented over the owner's own query handlers, and missing data is an answer, never an exception or a silent default. Reacting
  to another module's change uses its integration events (see `events`). A local copy fed by events is allowed only under CRIT-05.
- **Why:** the owner keeps its rules and its model private; a query port answers with current data without a second source of truth.
- **Enforced by:** `LayeringTests.A_module_contract_exposes_nothing_but_its_public_types` (planned) + ARCH-05 test
  (both planned); review for read-only and "missing is an answer".
- **Good:** `IOrdersQueries.IsBusinessDayAsync(...)` answers `CalendarStatus.NotCovered` for a calendar Orders does not hold. **Bad:** a port method
  that writes, returns an Orders aggregate, or answers "no shipments" when Orders has no data.
- **Status:** in force.

## ARCH-07 · Published contracts only grow

- **Rule:** Same as EVT-02 (canonical card in `events.md`); read it there.
- **Status:** see EVT-02.

## ARCH-08 · Integration event names are `frappe.<module>.<fact>.v<N>` and never change

- **Rule:** Same as EVT-01 (canonical card in `events.md`); read it there.
- **Status:** see EVT-01.

## ARCH-09 · Domain events become integration events only in Infrastructure

- **Rule:** Same as EVT-03 (canonical card in `events.md`); read it there.
- **Status:** see EVT-03.

## ARCH-10 · One module, one schema, one data-only login role

- **Rule:** each module owns one schema, its migrations history inside it, one DbContext, and a data-only login role `frappe_<module>` with grants on
  its own schema only. A narrower extra role (`frappe_<module>_relay`) exists only when a process needs less than the module role.
- **Why:** the database enforces ARCH-05; a module cannot read another's tables even by mistake.
- **Enforced by:** Migrator (`DatabaseMigration` + `ModuleRoles`: no DDL, no other schema); review.
- **Good:** `BillingMigration.Role = "frappe_billing"`, `RelayRole = "frappe_billing_relay"`. **Bad:** an Orders query joining `billing.principals`.
- **Status:** in force.

## ARCH-11 · Composition happens only in hosts, from explicit module lists

- **Rule:** each host lists its modules in one `Modules.cs`, one entry per module, and names nothing from a module but its door (STR-11):
  `<M>Module.Api` (an `IHttpModule`, in the Api's `Endpoints`) and `<M>Module.Worker` (an `IModule`, in the Worker's `All`). The Migrator is the
  exception: it lists `<M>Database : IDatabaseModule` from Infrastructure and stays without ASP.NET Core. The facets behind a door
  (`IModule`, `Frappe.Modules`; `IHttpModule` with `MapEndpoints(v1)`, `Frappe.Http`) are composed only by the door, through `ComposedModule` and
  `ComposedHttpModule`. Modules are never discovered by assembly scanning (`AddHandlers(assembly)` inside a module stays allowed). A door never
  names another module's door, a module never registers another module, nor reads host configuration outside its own section.
- **Why:** each host decides what runs where and reads a module from one file; adding a module is one line per host. The Worker loads the door's
  Http project without mapping it.
- **Enforced by:** `ModuleDoorTests` (hosts name only doors, a door exposes only `<M>Module`, no door references another door; Notifications
  allow-listed until it gets its door), `WorkerCompositionTests` (the Worker maps no endpoint and registers no attempt limiter),
  `HostReferenceTests` (the Migrator never references ASP.NET Core), `<M>ModuleTests` (a door registers what its facets did); review.
- **Good:** `Endpoints = [AccessModule.Api, IdentityModule.Api, ...]` in the Api and `All = [AccessModule.Worker, IdentityModule.Worker, ...]` in
  the Worker; the Api's console origin check asks the `IBrowserCredential`s the modules registered (`IdentityHttp` registers the session
  cookie), so the host names no module's credential. **Bad:** a host listing `new AccessCoreServices(), new AccessWorkerServices()` itself, or
  registering a module's adapter (`IdentityMessagePurge` in `WorkerComposition`); `AccessModule` calling `IdentityModule.Worker.AddServices()`.
- **Status:** in force.

## ARCH-12 · Modules emit no spans or exporters; business metrics only

- **Rule:** tracing and exporters are platform work; modules declare business metrics through `IMeterFactory` only (see `observability`).
- **Why:** one telemetry pipeline, configured once.
- **Enforced by:** `LayeringTests.Modules_contain_no_tracing_or_exporter_code` (planned; cover every module project, Orders.Http and the Orders source
  packages included).
- **Good:** `OrdersMetrics` over `IMeterFactory`. **Bad:** `new ActivitySource("Frappe.Orders")` in a handler.
- **Status:** in force.

## ARCH-13 · Application ports never save

- **Rule:** no Application port declares `SaveChangesAsync`; the unit of work commits each command once (see `uow`).
- **Why:** a port that saves commits half a command.
- **Enforced by:** `LayeringTests.Application_ports_leave_saving_to_the_unit_of_work` (planned).
- **Good:** `IPrincipals.Add(principal)`. **Bad:** `IPrincipals.SaveAsync()`.
- **Status:** in force.

## ARCH-14 · Plug-in packages depend on their core, never the reverse

- **Rule:** a plug-in package (region, source, producer) references its module's core and declares; the core never references a plug-in. Names follow
  STR-04.
- **Why:** adding a region or a producer adds a package; it never edits the core.
- **Enforced by:** `LayeringTests.Source_packages_declare_and_never_reach_the_pipeline_or_the_database`, `Orders_core_never_knows_a_source_package`,
  `Mailing_core_knows_no_producer_module` (all planned: no Orders or Mailing module exists yet).
- **Good:** `Frappe.Orders.Regions.Argentina` → Orders core. **Bad:** `OrdersModule` calling `AddArgentinaCalendar()`.
- **Status:** in force.

## ARCH-15 · Namespaces follow folders

- **Rule:** `RootNamespace` equals the project name; every namespace equals root + folder path; request types live in `Frappe.<Module>.*`.
  `OutboxMessage` and
  `InboxMessage` keep namespace `Frappe.Messaging` inside the `Frappe.Messaging.Storage` assembly because every module's EF snapshot records that
  CLR name.
- **Why:** the handler pipeline derives the module from the namespace; folders and namespaces never drift.
- **Enforced by:** IDE0130 (warning).
- **Good:** `Frappe.Orders.Application.Regions.GetRegion`. **Bad:** `namespace Frappe.Orders;` in `Regions/GetRegion/`.
- **Status:** in force.

## ARCH-16 · Durable consumer names are permanent

- **Rule:** Same as EVT-08 (canonical card in `events.md`); read it there.
- **Status:** see EVT-08.

## ARCH-17 · One type per file

- **Rule:** one top-level type per file, the file named after it, in all of `src/` and `tests/` (see `craft`).
- **Why:** types are found by name; diffs stay local.
- **Enforced by:** SA1402/SA1649 (today only on folders enabled in `.editorconfig`; repo-wide planned).
- **Good:** `RegionView.cs` holds `RegionView`. **Bad:** a `Views.cs` with five records.
- **Status:** in force.

## ARCH-18 · Platform holds no business rules and never knows a module

- **Rule:** `src/Platform/*` holds technical capabilities with no business vocabulary and never references `src/Modules/*`. A piece moves there only
  under CRIT-02.
- **Why:** a business rule in platform couples every module to one module's domain.
- **Enforced by:** ArchTest "platform never references a module" (planned); review for business vocabulary.
- **Good:** `Frappe.Caching.CacheInvalidator`. **Bad:** a `ShipmentHelper` in `Frappe.UseCases`.
- **Status:** in force.
