# CQRS rules (CQRS)

- CQRS-01 · A use case is a command or a query, one folder each
- CQRS-02 · One handler per request; adapters depend on the port
- CQRS-03 · Cross-cutting concerns are pipeline decorators
- CQRS-04 · Never rewrite the composed handler graph
- CQRS-05 · Commit rejections become a `Result` in the transactional decorator
- CQRS-06 · A command returns only what its answer needs from the same write
- CQRS-07 · Queries never write
- CQRS-08 · Commands use repositories; queries use read ports
- CQRS-09 · Business writes only through commands
- CQRS-10 · Retryable creation commands accept `Idempotency-Key`, opt-in
- CQRS-11 · Projections follow their source, and only when a query needs them

### CQRS-01 · A use case is a command or a query, one folder each
- **Rule:** A use case lives in `Application/<Feature>/<UseCase>/` with `<UseCase>.cs` (a `public sealed record` implementing `ICommand<T>` to change state or `IQuery<T>` to read) and `<UseCase>Handler.cs` (`internal sealed`, primary constructor, `ICommandHandler<,>` or `IQueryHandler<,>`). Names are imperative for commands (`AddSubdivisionAlias`) and `Get*`/`List*`/`Find*` for queries.
- **Why:** The intent and the side effects are visible from the type, and the pipeline treats each kind correctly (CQRS-03).
- **Enforced by:** review; ArchTest (planned): request records public sealed in `Frappe.<Module>.Application.*`, handlers internal sealed in the same namespace.
- **Good:** `Geo.Application/Territory/AddSubdivisionAlias/{AddSubdivisionAlias.cs, AddSubdivisionAliasHandler.cs}`. **Bad:** `SettleLostRegistration.cs` sitting inside `RegisterOwner/`; a `public class` handler.
- **Status:** in force.

### CQRS-02 · One handler per request; adapters depend on the port
- **Rule:** Each request has exactly one handler, registered by `AddHandlers`. Endpoints, jobs and consumers resolve `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`, never the handler class, and never call another handler's class.
- **Why:** Resolving the port is what gives every call the pipeline (unit of work, instrumentation); a class reference bypasses it.
- **Enforced by:** `HandlerPipelineTests` `A_second_handler_for_the_same_port_is_rejected_at_registration`; LayeringTests `The_http_adapter_reaches_use_cases_only_through_the_application_layer`.
- **Good:** `ICommandHandler<AddSubdivisionAlias, SubdivisionAliasView> handler` injected into the endpoint. **Bad:** `new AddSubdivisionAliasHandler(...)` or injecting `AddSubdivisionAliasHandler`.
- **Status:** in force.

### CQRS-03 · Cross-cutting concerns are pipeline decorators
- **Rule:** Commands run `Instrumented → Transactional → handler`; queries run `Instrumented → handler`. A new cross-cutting concern (idempotency, authorization of a use case) becomes a decorator listed in `HandlerRegistration.Ports`, never code repeated in handlers. **Resource-based authorization** is the one form that is not a decorator: when the decision needs the row the handler loads (404 before 403 on a tenant resource, a field of the row such as the creator), the handler loads it through its own port and makes exactly one call to the module's authorizer with that resource (the shape of ASP.NET Core's `IAuthorizationService.AuthorizeAsync(user, resource, requirement)`); the authorizer owns parsing, the ask to Access and the refusal, so handlers never repeat those steps.
- **Why:** One place decides order and behavior for every use case; a span and duration always include the commit. A decorator cannot see a row it has not loaded, and loading it twice costs a query and opens a race between the check and the write (https://learn.microsoft.com/aspnet/core/security/authorization/resourcebased).
- **Enforced by:** `HandlerPipelineTests`; `UnitOfWorkPipelineTests` `A_query_never_touches_the_unit_of_work`.
- **Good:** `(typeof(ICommandHandler<,>), [typeof(TransactionalCommandHandler<,>), typeof(InstrumentedCommandHandler<,>)])`. **Bad:** `using var activity = source.StartActivity(...)` inside a handler; a handler that parses the id, asks Access and builds the 404/403 itself instead of one call to the module's authorizer.
- **Status:** in force.

### CQRS-04 · Never rewrite the composed handler graph
- **Rule:** No module removes, replaces or wraps a handler registration that `AddHandlers` composed. A step around a command is a platform decorator (CQRS-03), an in-unit domain event handler, or an after-commit handler (UOW-09).
- **Why:** A module-level rewrite silently changes the pipeline order for one command and breaks when registration order changes.
- **Enforced by:** review.
- **Good:** settling a lost registration race through `ICommitRejection` handled by the pipeline (CQRS-05). **Bad:** `HandlerWrapping.Wrap<RegisterOwner, RegisteredOwner>(services, ...)` doing `services.Remove(composed)`.
- **Status:** in force.

### CQRS-05 · Commit rejections become a `Result` in the transactional decorator
- **Rule:** An expected refusal at commit (concurrency conflict, unique or check constraint) is an `ICommitRejection`; `TransactionalCommandHandler` catches it, rolls back and returns `Result.Failure(rejection.Answer)`. Endpoints call the port and map the `Result`; no `<Module>Commands` helper catches rejections.
- **Why:** Every command answers a lost race the same way, and adapters stay one line.
- **Enforced by:** `UnitOfWorkPipelineTests` (planned case: a commit rejection is rolled back and answered as a failed `Result`).
- **Good:** `(await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(a => TypedResults.Created(...))` in the endpoint, nothing else. **Bad:** `IdentityCommands.HandleAsync(...)` catching `ICommitRejection`; `GeoCommands` catching concrete exception types in Http.
- **Status:** in force.

### CQRS-06 · A command returns only what its answer needs from the same write
- **Rule:** A command returns `Unit`, the id it created, or the resource it just wrote when the HTTP contract answers with it (PATCH → 200 with the resource). It never reads extra data to enrich the answer; anything else is a query.
- **Why:** Commands stay cheap and their result cannot drift from what was committed.
- **Enforced by:** review.
- **Good:** `AddSubdivisionAlias : ICommand<SubdivisionAliasView>` built from the alias it added. **Bad:** a command handler that loads the country's whole territory to return a list view.
- **Status:** in force.

### CQRS-07 · Queries never write
- **Rule:** A query handler never changes state, directly or through a port; a write it seems to need is a separate command its adapter dispatches.
- **Why:** Queries run outside any unit of work: a write there autocommits, skips outbox and events, and makes reads unsafe to retry or cache.
- **Enforced by:** ArchTest (planned): no `IQueryHandler` depends on a repository or write port.
- **Good:** the authenticator runs `AuthenticateSession` (read), then dispatches a `TouchSession` command. **Bad:** `AuthenticateSessionHandler` calling `ISessions.TouchAsync` (`ExecuteUpdate`, autocommit).
- **Status:** in force.

### CQRS-08 · Commands use repositories; queries use read ports
- **Rule:** Command handlers use repositories that return tracked aggregates and stage new ones (`FindAsync`, `Add`). Query handlers use read ports that project straight to `*View` with `AsNoTracking`, and never load an aggregate. No port mixes both.
- **Why:** Reads stay cheap and shaped for the answer; repositories stay the only door to invariant-guarding objects.
- **Enforced by:** ArchTest (planned): query handlers depend on no repository port; review for mixed ports.
- **Good:** `IPrincipals.FindAsync` used only by commands; a `IOwnerProfiles.GetMeAsync` read port projecting to `Me`. **Bad:** `GetMeHandler(IPrincipals principals)` loading `Principal`; `IHolidayCalendars` with tracked `FindAsync` and projected `CoverageAsync`.
- **Status:** in force.

### CQRS-09 · Business writes only through commands
- **Rule:** Every business state change is a command with its unit of work, scheduled syncs included. Only technical maintenance (purging expired rows, claiming work) may be set-based SQL in an Infrastructure job; that job is idempotent and its XML doc says what it touches and why it is not a command.
- **Why:** A write outside the pipeline skips validation, domain events, outbox and metrics.
- **Enforced by:** LayeringTests `Application_ports_leave_saving_to_the_unit_of_work`; review for Infrastructure `SaveChangesAsync` calls.
- **Good:** `MessagePurger<TContext>` and `CredentialPurger` (idempotent batched deletes, documented). **Bad:** `SnapshotSync` staging a run and calling `db.SaveChangesAsync` itself.
- **Status:** in force.

### CQRS-10 · Retryable creation commands accept `Idempotency-Key`, opt-in
- **Rule:** A creation endpoint a client may retry opts in to the platform `Idempotency-Key` piece: the first response is stored for 24 h and replayed for the same key, and the header is documented in OpenAPI. Modules never hand-roll their own deduplication for HTTP retries.
- **Why:** A timeout on POST must be safe to retry without creating twice.
- **Enforced by:** review; platform tests once the piece exists.
- **Good:** (planned) `.RequireIdempotencyKey()` on the route plus the header in the OpenAPI operation. **Bad:** a handler checking "created in the last minute with the same body".
- **Contract:** opt in with `.AcceptsIdempotencyKey()`; a 2xx answer is stored 24 h and replayed; the same key with another body answers 422; a request still running with that key answers 409 `in_progress`; the store being down answers 503 when the request carries a key (requests without it are unaffected).
- **Status:** in force.

### CQRS-11 · Projections follow their source, and only when a query needs them
- **Rule:** Build a projection only when a query justifies it. A projection of the module's own data is updated by an `IDomainEventHandler` in the same unit of work; one of another module's data is fed by that module's integration events and states its consistency window in its XML doc.
- **Why:** Own projections never drift from their aggregate; foreign copies are honest about staleness.
- **Enforced by:** review.
- **Good:** an `IDomainEventHandler<OwnerRegistered>` updating an owners-by-email view in the same save. **Bad:** a nightly job rebuilding a projection of own data; a projection with no query reading it.
- **Status:** in force.
