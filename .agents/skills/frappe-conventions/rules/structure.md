# Structure rules

Where files go and what they are called. Layer dependencies: `architecture`. Use-case shape: `cqrs`, `domain`.

- STR-01 · Every module has the same project set
- STR-02 · Application is organized by feature, then one folder per use case
- STR-03 · Infrastructure is organized by feature plus Persistence/, Messaging/, Retention/ and Telemetry/ only
- STR-04 · Plug-in packages are named `Frappe.<Module>.<Kind>.<Name>`
- STR-05 · Use cases are named for intent; handlers add `Handler`
- STR-06 · Ports are capability nouns; adapters are prefixed by technology
- STR-07 · Aggregates live only in Domain and are mapped directly
- STR-08 · `*Row` names only data without domain behavior
- STR-09 · Each EF configuration sits next to the type it maps
- STR-10 · `InternalsVisibleTo` only toward the module's own tests, its own door and the host test project that composes it
- STR-11 · Each layer exposes its host facet from one fixed class
- STR-12 · No module rewrites the composed handler graph
- STR-13 · Tests mirror the production projects
- STR-14 · Each module README matches its code

## STR-01 · Every module has the same project set

- **Rule:** `src/Modules/<M>/` holds `Frappe.<M>` (the door, STR-11), `Frappe.<M>.Domain`, `.Application`, `.Contracts`, `.Infrastructure`, `.Http`,
  plus plug-in packages (STR-04). Every module; no module invents a different layout.
- **Why:** one shape to navigate; ArchTests and tools target every module the same way (same model for all: see `domain`, `cqrs`).
- **Enforced by:** `ModuleDoorTests` (the door); `StructureTests.Every_module_has_the_door_and_the_five_layers` (the project set over `src/Modules`).
- **Good:** `src/Modules/Access/` with `Frappe.Access` and its five layer projects; `src/Modules/Identity/` with `Frappe.Identity`, its five layer
  projects and the plug-in `Extensions/Frappe.Identity.Limits.Valkey`; `src/Modules/Notifications/` with `Frappe.Notifications`, its five layer
  projects and the plug-ins under `Extensions/`
  (`Frappe.Notifications.Senders.Email`, `.Messages.Account`, `.Messages.Marketing`,
  `.Triggers.Identity`). **Bad:** a module with no Contracts or Http project, or no door.
- **Status:** in force.

## STR-02 · Application is organized by feature, then one folder per use case

- **Rule:** `Application/<Feature>/<UseCase>/` holds `<UseCase>.cs` (public sealed record request) and `<UseCase>Handler.cs` (internal sealed); ports,
  views and exceptions shared by the feature sit loose in `<Feature>/`; only module-wide types (`<M>Errors`, `<M>QueriesRegistration`, a type several
  features use) sit at the project root, and the module's metrics sit in `Telemetry/` (OBS-02). A helper used by one use case lives in that use
  case's folder.
- **Why:** a feature is read and changed in one place; deleting a use case deletes one folder.
- **Enforced by:** review only.
- **Good:** `Identity.Application/Sessions/LogOut/{LogOut,LogOutHandler}.cs`, `Sessions/ISessions.cs`. **Bad (hypothetical):** one use case's
  helper at the project root; `ISessions` inside one use case's folder.
- **Status:** in force.

## STR-03 · Infrastructure is organized by feature plus Persistence/, Messaging/, Retention/ and Telemetry/ only

- **Rule:** feature folders hold their adapters (`Ef*`, `Cached*`, `Valkey*`), cached records and EF configurations. Only module-wide pieces go in the
  four fixed technical folders: `Persistence/` (DbContext, design-time factory, migration runner, migrations, database options), `Messaging/`
  (translator, consumers, relay wiring), `Retention/` (purge jobs and their options) and `Telemetry/` (`<M>TelemetryRegistration`: the metrics
  classes' services and the `CountOnCommit` lines, OBS-02). No other technical folder.
- **Why:** a feature's storage is found next to the feature; technical folders do not become dumping grounds.
- **Enforced by:** review.
- **Good:** `Identity.Infrastructure/Owners/{OwnerConfiguration,EfOwners,EfOwnerReads}.cs`,
  `Identity.Infrastructure/Telemetry/IdentityTelemetryRegistration.cs`. **Bad (hypothetical):**
  `<M>.Infrastructure/Configuration/<M>DatabaseOptions.cs` (belongs in `Persistence/`).
- **Status:** in force.

## STR-04 · Plug-in packages live in `Extensions/` as `Frappe.<Module>.<Kind>.<Name>`

- **Rule:** a package that plugs into a module's core is `Frappe.<Module>.<Kind>.<Name>` in `src/Modules/<Module>/Extensions/`, with `<Kind>` plural
  and naming what the package does (`Messages`, `Senders`, `Triggers`). Dependency direction: ARCH-14.
- **Why:** the name says what the package plugs in, and one folder lists every plug-in of the module.
- **Enforced by:** `StructureTests.Every_project_of_a_module_is_its_door_a_layer_or_a_named_plug_in`.
- **Good:** `src/Modules/Notifications/Extensions/Frappe.Notifications.Senders.Email`, `Frappe.Notifications.Messages.Account`,
  `Frappe.Notifications.Triggers.Identity`, `src/Modules/Identity/Extensions/Frappe.Identity.Limits.Valkey`. **Bad:**
  (hypothetical) `src/Modules/Notifications/Channels/Frappe.Notifications.Channels.Email` (a kind folder beside the module) and
  `Frappe.Notifications.Identity` (→ `Frappe.Notifications.Triggers.Identity`).
- **Status:** in force.

## STR-05 · Use cases are named for intent; handlers add `Handler`

- **Rule:** a request is a verb phrase in the business language (`GetMe`, `ListMySessions`, `RegisterOwner`); its handler is `<Request>Handler`.
  Response naming: see `cqrs`.
- **Why:** the folder listing reads as the module's capabilities.
- **Enforced by:** `StructureTests.Every_handler_is_named_for_its_request`; review.
- **Good:** `GetCurrentSession` → `GetCurrentSessionHandler`. **Bad (hypothetical):** `SessionQuery`, `SessionService.Get`.
- **Status:** in force.

## STR-06 · Ports are capability nouns; adapters are prefixed by technology

- **Rule:** an Application port names a capability (`IOwners`, `ISessions`, `IAttemptLimiter`); its adapter is `<Technology><Capability>`: `Ef*` (EF
  Core), `Cached*` (cache decorator), `Valkey*` (Valkey state), `Hmac*`, `Resend*`, and so on.
- **Why:** the name tells which dependency an adapter wraps and what to swap.
- **Enforced by:** review.
- **Good:** `ISessions` → `EfSessions`; `IAttemptLimiter` → the Valkey adapter in `Frappe.Identity.Limits.Valkey`. **Bad (hypothetical):**
  `SessionsImpl`, `SessionRepository2`.
- **Status:** in force.

## STR-07 · Aggregates live only in Domain and are mapped directly

- **Rule:** only Domain types extend `AggregateRoot`. Infrastructure maps them as they are with fluent configuration (STR-09): private constructors
  and setters, no EF attributes or EF types in Domain, no parallel persistence class for an aggregate.
- **Why:** persistence ignorance keeps one model of the business; a second persistence class drifts from it.
- **Enforced by:** `StructureTests.Only_domain_types_extend_AggregateRoot`; `LayeringTests.Inner_layers_know_nothing_about_infrastructure` (no EF in
  Domain).
- **Good:** `Owner` (`Frappe.Identity.Domain/Owners`) mapped by `OwnerConfiguration`. **Bad (hypothetical):**
  `internal sealed class SessionRow : AggregateRoot` in Infrastructure.
- **Status:** in force.

## STR-08 · `*Row` names only data without domain behavior

- **Rule:** `*Row` is an Infrastructure persistence type for data with no business behavior: reference catalogs, read projections, technical records.
  It never extends `AggregateRoot` and never raises domain events.
- **Why:** the suffix tells a reader there are no rules to look for.
- **Enforced by:** `StructureTests.No_Row_type_extends_AggregateRoot`.
- **Good (hypothetical, no `*Row` type exists yet):** `CountryNameRow`, a reference catalog entry. **Bad (hypothetical):** `SessionRow` extending
  `AggregateRoot`.
- **Status:** in force.

## STR-09 · Each EF configuration sits next to the type it maps

- **Rule:** one `IEntityTypeConfiguration<T>` per mapped type, named `<Type>Configuration`, in the feature folder of that type; the DbContext applies
  configurations from its assembly and holds only mapping.
- **Why:** mapping changes with its feature, not in a shared folder.
- **Enforced by:** review.
- **Good:** `Identity.Infrastructure/Sessions/SessionConfiguration.cs`, `Notifications.Infrastructure/Deliveries/DeliveryConfiguration.cs`.
  **Bad (hypothetical):** `Identity.Infrastructure/Persistence/SessionConfiguration.cs`, `OwnerConfiguration.cs`.
- **Status:** in force.

## STR-10 · `InternalsVisibleTo` only toward the module's own tests, its own door and the host test project that composes it

- **Rule:** declare it only as `<InternalsVisibleTo>` in the csproj, and only toward: the same module's (or platform's) test projects
  (`Frappe.<M>.UnitTests`, `.IntegrationTests`); from Infrastructure, the module's own door `Frappe.<M>` (STR-11), which joins internal ports to
  what Infrastructure may not reference; the module's own Infrastructure from its Application (`Frappe.Access.Application`); and the host
  integration-test project that composes the module (`Frappe.Api.IntegrationTests`, from `Http` and `Infrastructure`). Never toward a host or
  another production assembly, never as an `[assembly:]` attribute.
- **Why:** internals stay internal; a production consumer must go through a public contract, and the door is part of the module.
- **Enforced by:** `StructureTests.InternalsVisibleTo_only_names_the_targets_STR_10_allows` and
  `No_source_declares_InternalsVisibleTo_as_an_assembly_attribute`.
- **Good:** `Frappe.Identity.Infrastructure.csproj` → `Frappe.Identity.UnitTests`, `Frappe.Identity.IntegrationTests`,
  `Frappe.Api.IntegrationTests` and `Frappe.Identity` (its `IdentityMessagePurge` implements the internal `IMessagePurge`);
  `Frappe.Identity.Http.csproj` → `Frappe.Api.IntegrationTests`; `Frappe.Notifications.Infrastructure.csproj` → `Frappe.Notifications` for
  `NotificationsMessagePurge`. **Bad (hypothetical):** `Frappe.Notifications.Infrastructure` → `Frappe.Worker`.
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
  composing `NotificationsCoreServices`, `NotificationsWorkerServices`, the kinds, `IdentityTriggersModule`, `EmailSenderWorkerModule` and its
  internal message purge. **Bad:** a host registering
  `AccessGate` itself, or listing `AccessCoreServices` next to `AccessModule.Api`, or `new AccountMessagesModule()` next to `NotificationsModule.Api`.
- **Status:** in force.

## STR-12 · No module rewrites the composed handler graph

- **Rule:** Same as CQRS-04 (canonical card in `cqrs.md`); read it there.
- **Status:** see CQRS-04.

## STR-13 · Tests mirror the production projects

- **Rule:** `tests/Modules/<M>/Frappe.<M>.UnitTests` and `.IntegrationTests`; platform has `Frappe.Platform.UnitTests` and `.IntegrationTests`;
  cross-cutting suites in `tests/Frappe.Architecture.Tests` and `tests/Frappe.EndToEnd.Tests`; shared helpers in `tests/Shared/Frappe.Testing` (see
  `tests`).
- **Why:** unit and integration suites run with different dependencies; the split is checkable.
- **Enforced by:** review only.
- **Good:** `tests/Modules/Identity/Frappe.Identity.UnitTests` and `.IntegrationTests`. **Bad (hypothetical):** `tests/Platform/Frappe.Platform.Tests`
  mixing both.
- **Status:** in force.

## STR-14 · Each module README matches its code

- **Rule:** `src/Modules/<M>/README.md` keeps the sections Doors, Features, Metrics, Events and Extensions. Every relative link resolves, the
  Metrics table lists exactly the instruments on the module's meter with the class that creates each, and the Extensions table lists exactly
  the projects under `Extensions/`.
- **Why:** the README is where a reader starts; a table that drifts from the code sends them to the wrong file.
- **Enforced by:** `ReadmeDriftTests` (links, metrics, extensions); review for Doors, Features and Events.
- **Good:** `frappe.notifications.provider_requests` listed with `EmailProviderMetrics`, the class in `Senders.Email` that creates it.
  **Bad:** a row for a renamed counter, or a link to a file that moved.
- **Status:** in force.
