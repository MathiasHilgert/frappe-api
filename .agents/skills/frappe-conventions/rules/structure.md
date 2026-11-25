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

## STR-01 · Every module has the same project set

- **Rule:** `src/Modules/<M>/` holds `Frappe.<M>` (the door, STR-11), `Frappe.<M>.Domain`, `.Application`, `.Contracts`, `.Infrastructure`, `.Http`,
  plus plug-in packages (STR-04). Every module; no module invents a different layout.
- **Why:** one shape to navigate; ArchTests and tools target every module the same way (same model for all: see `domain`, `cqrs`).
- **Enforced by:** `ModuleDoorTests` (the door); module project-set ArchTest over `Frappe.slnx` (planned).
- **Good:** `src/Modules/Access/` with `Frappe.Access` and its five layer projects; `src/Modules/Identity/` with `Frappe.Identity`, its five layer
  projects and the plug-in `Limits/Frappe.Identity.Limits.Valkey`; `src/Modules/Notifications/` with `Frappe.Notifications`, its five layer
  projects and the plug-ins under `Channels/`, `Kinds/` and `Producers/`. **Bad:** a module with no Contracts or Http project, or no door.
- **Status:** in force.

## STR-02 · Application is organized by feature, then one folder per use case

- **Rule:** `Application/<Feature>/<UseCase>/` holds `<UseCase>.cs` (public sealed record request) and `<UseCase>Handler.cs` (internal sealed); ports,
  views and exceptions shared by the feature sit loose in `<Feature>/`; only module-wide types (`<M>Errors`, `<M>Metrics`, `<M>QueriesRegistration`, a
  type several features use) sit at the project root. A helper used by one use case lives in that use case's folder.
- **Why:** a feature is read and changed in one place; deleting a use case deletes one folder.
- **Enforced by:** review (folder-shape ArchTest planned).
- **Good:** `Orders.Application/Regions/GetRegion/{GetRegion,GetRegionHandler}.cs`, `Regions/IRegionCatalog.cs`. **Bad:** one use case's helper
  at the project root; a port shared by the feature inside one use case's folder.
- **Status:** in force.

## STR-03 · Infrastructure is organized by feature plus Persistence/ and Messaging/ only

- **Rule:** feature folders hold their adapters (`Ef*`, `Cached*`, `Valkey*`), cached records and EF configurations. Only module-wide pieces go in the
  two fixed technical folders: `Persistence/` (DbContext, design-time factory, migration runner, migrations, database options) and `Messaging/`
  (translator, consumers, relay wiring). No other technical folder.
- **Why:** a feature's storage is found next to the feature; technical folders do not become dumping grounds.
- **Enforced by:** review.
- **Good:** `Orders.Infrastructure/Regions/{RegionRow,RegionRowConfiguration,EfRegionCatalog,CachedRegionCatalog}.cs`. **Bad:**
  `<M>.Infrastructure/Configuration/<M>DatabaseOptions.cs` (belongs in `Persistence/`).
- **Status:** in force.

## STR-04 · Plug-in packages are named `Frappe.<Module>.<Kind>.<Name>`

- **Rule:** a package that plugs into a module's core is `Frappe.<Module>.<Kind>.<Name>` in `src/Modules/<Module>/<Kind>/`, with `<Kind>` plural
  (`Regions`, `Sources`, `Producers`). Dependency direction: ARCH-14.
- **Why:** the name says what the package plugs in and where its siblings live.
- **Enforced by:** `PlugInNamingTests` (planned) (tests/Frappe.Architecture.Tests); review.
- **Good:** `Frappe.Orders.Regions.Argentina`. **Bad:** `Frappe.Orders.Global` (→ `Frappe.Orders.Sources.Global`), `Frappe.Mailing.Billing` (→
  `Frappe.Mailing.Producers.Billing`).
- **Status:** in force.

## STR-05 · Use cases are named for intent; handlers add `Handler`

- **Rule:** a request is a verb phrase in the business language (`GetRegion`, `ListRegions`, `RegisterOwner`); its handler is `<Request>Handler`.
  Response naming: see `cqrs`.
- **Why:** the folder listing reads as the module's capabilities.
- **Enforced by:** review (handler-suffix ArchTest planned).
- **Good:** `GetRegion` → `GetRegionHandler`. **Bad:** `RegionQuery`, `RegionService.Get`.
- **Status:** in force.

## STR-06 · Ports are capability nouns; adapters are prefixed by technology

- **Rule:** an Application port names a capability (`IPrincipals`, `IRegionCatalog`, `IMailer`); its adapter is `<Technology><Capability>`: `Ef*` (EF
  Core), `Cached*` (cache decorator), `Valkey*` (Valkey state), `Hmac*`, `Resend*`, and so on.
- **Why:** the name tells which dependency an adapter wraps and what to swap.
- **Enforced by:** review.
- **Good:** `IRegionCatalog` → `EfRegionCatalog`, `CachedRegionCatalog`. **Bad:** `RegionCatalogImpl`, `RegionRepository2`.
- **Status:** in force.

## STR-07 · Aggregates live only in Domain and are mapped directly

- **Rule:** only Domain types extend `AggregateRoot`. Infrastructure maps them as they are with fluent configuration (STR-09): private constructors
  and setters, no EF attributes or EF types in Domain, no parallel persistence class for an aggregate.
- **Why:** persistence ignorance keeps one model of the business; a second persistence class drifts from it.
- **Enforced by:** ArchTest "only Domain extends AggregateRoot" (planned); `LayeringTests.Inner_layers_know_nothing_about_infrastructure` (no EF in
  Domain).
- **Good:** `Principal` mapped by `PrincipalConfiguration`. **Bad:** `internal sealed class RegionRow : AggregateRoot` in Infrastructure.
- **Status:** in force.

## STR-08 · `*Row` names only data without domain behavior

- **Rule:** `*Row` is an Infrastructure persistence type for data with no business behavior: reference catalogs, read projections, technical records.
  It never extends `AggregateRoot` and never raises domain events.
- **Why:** the suffix tells a reader there are no rules to look for.
- **Enforced by:** `LayeringTests.Only_sealed_types_in_a_domain_project_derive_from_AggregateRoot` (planned).
- **Good:** `RegionNameRow`. **Bad:** `RegionRow`, `SubdivisionRow`, `SyncRunRow` extending `AggregateRoot`.
- **Status:** in force.

## STR-09 · Each EF configuration sits next to the type it maps

- **Rule:** one `IEntityTypeConfiguration<T>` per mapped type, named `<Type>Configuration`, in the feature folder of that type; the DbContext applies
  configurations from its assembly and holds only mapping.
- **Why:** mapping changes with its feature, not in a shared folder.
- **Enforced by:** review.
- **Good:** `Orders.Infrastructure/Regions/RegionRowConfiguration.cs`. **Bad:** `Billing.Infrastructure/Persistence/PrincipalConfiguration.cs`,
  `SessionConfiguration.cs`, `VerificationCodeConfiguration.cs`.
- **Status:** in force.

## STR-10 · `InternalsVisibleTo` only toward the module's own test projects

- **Rule:** declare it only as `<InternalsVisibleTo>` in the csproj, only toward the same module's (or platform's) test projects and, from
  Infrastructure, toward the module's own door `Frappe.<M>` (STR-11), which joins internal ports to what Infrastructure may not reference; never
  toward a host or another production assembly, never as an `[assembly:]` attribute.
- **Why:** internals stay internal; a production consumer must go through a public contract, and the door is part of the module.
- **Enforced by:** csproj ArchTest (planned).
- **Good:** `Frappe.Identity.Infrastructure.csproj` → `Frappe.Identity.UnitTests`, `Frappe.Identity.IntegrationTests` and `Frappe.Identity` (its
  `IdentityMessagePurge` implements the internal `IMessagePurge`); `Frappe.Notifications.Infrastructure.csproj` → `Frappe.Notifications` for
  `NotificationsMessagePurge`. **Bad:** `Frappe.Notifications.Infrastructure` → `Frappe.Worker`.
- **Status:** in force.

## STR-11 · Each layer exposes its host facet from one fixed class

- **Rule:** the door `Frappe.<M>` holds only `public static class <M>Module` with `Api` (an `IHttpModule`: the shared services, the Api-only
  services, then `<M>Http`, through `ComposedHttpModule`) and `Worker` (an `IModule`: the shared services, then the Worker-only services, through
  `ComposedModule`); it is the only thing a host names from the module (ARCH-11). Behind it, public facets that no host lists: Infrastructure
  `public sealed class <M>CoreServices : IModule` (every host), `<M>ApiServices : IModule` and `<M>WorkerServices : IModule` (one host each),
  `<M>Database : IDatabaseModule` (listed by the Migrator) with internal `<M>PersistenceRegistration` behind them; Application:
  `<M>QueriesRegistration` (the `I<M>Queries` facade); Http: `public sealed class <M>Http : IHttpModule` with route mapping; a plug-in: its own
  `IModule`, composed by the door. Handlers are registered by `AddHandlers(assembly)`, never one by one.
- **Why:** a module is read from one file, composition is found in one place per layer, and hosts list one entry per module (ARCH-11).
- **Enforced by:** `ModuleDoorTests`, `LayeringTests`; review.
- **Good:** `AccessModule.Worker = new ComposedModule(new AccessCoreServices(), new AccessWorkerServices())`; `IdentityModule.Api` composing
  `IdentityCoreServices`, `IdentityApiServices` and the plug-in `IdentityLimitsValkeyModule` before `IdentityHttp`; `NotificationsModule.Worker`
  composing `NotificationsCoreServices`, `NotificationsWorkerServices`, the kinds, `IdentityProducerModule`, `EmailChannelWorkerModule` and its
  internal message purge. **Bad:** a host registering
  `AccessGate` itself, or listing `AccessCoreServices` next to `AccessModule.Api`, or `new AccountKindsModule()` next to `NotificationsModule.Api`.
- **Status:** in force.

## STR-12 · No module rewrites the composed handler graph

- **Rule:** Same as CQRS-04 (canonical card in `cqrs.md`); read it there.
- **Status:** see CQRS-04.

## STR-13 · Tests mirror the production projects

- **Rule:** `tests/Modules/<M>/Frappe.<M>.UnitTests` and `.IntegrationTests`; platform has `Frappe.Platform.UnitTests` and `.IntegrationTests`;
  cross-cutting suites in `tests/Frappe.Architecture.Tests` and `tests/Frappe.EndToEnd.Tests`; shared helpers in `tests/Shared/Frappe.Testing` (see
  `tests`).
- **Why:** unit and integration suites run with different dependencies; the split is checkable.
- **Enforced by:** unit-test dependency check (planned).
- **Good:** `tests/Modules/<M>/Frappe.<M>.UnitTests`. **Bad:** `tests/Platform/Frappe.Platform.Tests` mixing both.
- **Status:** in force.
