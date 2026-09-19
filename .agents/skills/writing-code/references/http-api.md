# HTTP API

Package: `com.frappe.<module>.infrastructure.web`.

## Routes and access

One route is one class: a `@RestController` in `..infrastructure.web` with exactly one mapped method and an `@Access` posture (`com.frappe.platform.web`). That is the whole declaration:

```java
@RestController
@Access(Posture.AUTHENTICATED)
class CloseTabRoute {

    @PostMapping("/tabs/{tabId}/close")
    ResponseEntity<Void> close(@PathVariable UUID tabId, ResolvedSession caller) { … }
}
```

| Posture | Who may call | Refused with |
| --- | --- | --- |
| `PUBLIC` | anyone, with or without a token | never |
| `AUTHENTICATED` | any caller with a resolved session | 401 + `WWW-Authenticate: Bearer` |

**The HTTP layer authenticates, it never authorizes.** A posture only says whether a caller must be authenticated. Whether that caller may perform the operation (roles per branch, ownership, session kind) is authorization and belongs to the application layer: the route passes the `ResolvedSession` into the command or query, and the use case (through the bus) decides with the access module. Operations with only internal callers have no route at all.

- Startup fails, naming the class and the fix, for a route without `@Access`, with two or more mapped methods, or outside a package ending in `.infrastructure.web`.
- Annotated route classes are the only way to serve a path. Startup fails for any `RouterFunction` bean, any functional mapping with a router function, any handler mapping that serves paths outside the routes (a custom mapping, a bean named `/…`) and any non-actuator mapping ordered before the annotated routes; static resources are off (`spring.web.resources.add-mappings=false`). At runtime (defense in depth) a request no route serves passes through to 404/405 only when no other mapping would serve it, and a matched route is refused when a mapping ordered before the routes would take the request. Only `com.frappe` controllers are routes; framework controllers are not checked.
- The posture is enforced by one stateless Spring Security chain before any controller code runs (no session, cookies, CSRF or login form). The health endpoint, error dispatches and the OpenAPI paths are the only other public paths; other actuator endpoints are closed.
- Standard HTTP semantics (RFC 9110) for everything else: an unknown path answers 404 and a known path with an unsupported method 405 with `Allow`, with or without a session (route shapes are public in the OpenAPI spec). No controller code runs in either case. A framework controller the chain does not permit explicitly, or an ambiguous mapping, is refused (fail closed).
- The token comes from `Authorization: Bearer <token>` only; cookies and query parameters are never read. `SessionResolver` (identity) turns it into a `ResolvedSession` (principal id, session id, `SessionKind` `PERSON`/`TERMINAL`/`GUEST`), injected as a plain `ResolvedSession` parameter (only `AUTHENTICATED` routes may declare it; a `PUBLIC` route that does fails startup). Route classes depend on our kernel types only, never on Spring Security. A missing or unresolvable token leaves the caller anonymous: public routes still answer, authenticated ones 401. Until identity implements the resolver, no token resolves.
- CSRF protection is disabled deliberately (CodeQL `java/spring-disabled-csrf-protection` is a known, accepted finding): browsers never attach the bearer header automatically and no cookie or query token is read, so there is no ambient credential to forge.
- `/v1` is added once for all `com.frappe` controllers: map `/tabs/{tabId}/close`, serve `/v1/tabs/{tabId}/close`.
- The client address is `HttpServletRequest#getRemoteAddr()`: Tomcat takes it from `X-Forwarded-For` only when the connection comes from a trusted proxy (`FRAPPE_TRUSTED_PROXIES`).
- The OpenAPI spec (`/v3/api-docs`, every profile) marks every authenticated operation with the `bearer` scheme and documents 401; the Scalar API reference (`/scalar`) is served in `local` only.
- Tests register routes as `@Bean`s of a nested `@TestConfiguration` and call them with `MockMvcTester` and an `Authorization: Bearer` header resolved by a test `SessionResolver` bean.

## Endpoints

- REST under `/v1`, plural nouns, kebab-case paths: `POST /v1/sessions`, `DELETE /v1/sessions/current`.
- Controllers translate the request DTO (`record`) into the use case's input, call the use case, map `Result` → response. No business logic.
- `201 Created` with `Location` for creation, `204` for no-body success.
- Paginate collection endpoints (limit + cursor or page); never return unbounded lists.

## Validation

- Jakarta Bean Validation on request DTOs for shape (required, length, format).
- Business rules are validated in the domain and come back as `Result` failures.

## Errors (RFC 9457 problems)

Every failure is one shape, `application/problem+json` with `Content-Language`: `type` (stable URI `https://frappe.app/problems/<slug>`), `title` and `detail` (localized, `i18n.md`), `status`, `instance` (the request path), `code` (the slug), `params` (raw values, `{}` when none) and `traceId` (the request's OpenTelemetry trace id). Clients branch on `type` or `code`, never on text. Validation problems add `errors[]`.

```json
{"type": "https://frappe.app/problems/tab-already-closed", "title": "Tab already closed",
 "status": 409, "detail": "Tab T-12 is already closed.", "instance": "/v1/tabs/T-12/close",
 "code": "tab-already-closed", "params": {"tabId": "T-12"}, "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"}
```

### Business failures: `ProblemMapper`

A route turns a failed `Result` into `RequestRefusedException` (kernel, `com.frappe.platform.web`); one `ProblemMapper<E>` bean per failure type, in the module's `infrastructure.web`, decides status, slug and message key in one place:

```java
@PostMapping("/tabs/{tabId}/close")
ResponseEntity<Void> close(@PathVariable UUID tabId, ResolvedSession caller) {
    closeTab.close(new TabId(tabId), caller).orElseThrow(RequestRefusedException::new);
    return ResponseEntity.noContent().build();
}

@Component
class TabProblems implements ProblemMapper<TabError> {
    public Class<TabError> failureType() { return TabError.class; }

    public Problem problemOf(TabError error) {
        return switch (error) {
            case TabError.AlreadyClosed closed ->
                    Problem.of(409, "tab-already-closed", "order.tab.already-closed").with("tabId", closed.tabId());
            case TabError.NotFound notFound -> Problem.of(404, "tab-not-found", "order.tab.not-found");
        };
    }
}
```

- `Problem.of(status, slug, messageKey)`: status 4xx only (a business failure is never a server fault), slug kebab-case and stable once published, key in the module's catalogs. Text: `<messageKey>.title` and `<messageKey>.detail` in all three catalogs; `with(name, value)` params fill the detail's `{0}`, `{1}`… in order and are returned raw.
- Two mappers for the same failure type, or one for a subtype of another's, fail startup. A failure without a mapper, or a key missing from the catalogs, is a bug: the client gets the generic 500 problem and it is logged.
- Typical status: missing `404`, rule violation `409`/`422`, optimistic lock `409`.

### Platform problems

The platform answers everything no module maps (`com.frappe.platform.infrastructure.web`): `ProblemAdvice` (a `ResponseEntityExceptionHandler`, replacing Boot's), `SecurityRefusals` (the chain's entry point and access-denied handler, delegating to Spring MVC's exception resolvers) and `ProblemErrorController` (Boot's `ErrorController` for error dispatches, e.g. an exception thrown by a filter). The type follows the status, with text under `platform.problem.<slug>.title|detail`:

| Type (`code`) | Status | When |
| --- | --- | --- |
| `invalid-request` | 400 | unreadable body, bad parameters, validation (`errors[]`) |
| `unauthenticated` | 401 + `WWW-Authenticate: Bearer` | no token, or one that resolves to no session |
| `forbidden` | 403 | a caller with a session on a path the chain refuses (fail closed); routes never answer it |
| `not-found` | 404 | no route serves the path |
| `method-not-allowed` | 405 + `Allow` | the path exists, the method does not |
| `not-acceptable`, `content-too-large`, `unsupported-media-type` | 406, 413, 415 | content negotiation, body size and type |
| `request-rejected` | other 4xx | any other client error the framework raises |
| `internal-error` | 500 | anything unexpected (defect or infrastructure fault) |

- `internal-error` is generic on purpose: title "An unexpected error occurred", no params, and nothing of the cause (no exception message or class, SQL, technology or provider name, host, stack trace). The cause goes to one ERROR log line and the counter `http.server.unexpected.errors{error}` (`errors.md`); support finds it by the `traceId`.
- Validation (`@Valid @RequestBody`): one `errors[]` entry per violated constraint, `{"pointer": "/lines/0/quantity", "code": "min", "params": {"value": 1}, "detail": "must be at least 1"}`: RFC 6901 pointer into the body, constraint name in kebab-case, the constraint's attributes as params, detail from `platform.validation.<code>` (falling back to `platform.validation.invalid`; arguments are the attributes sorted by name).
- `server.error.include-stacktrace=never` and `include-message=never` stay set as defense in depth.

## OpenAPI and i18n

- OpenAPI is generated from code; annotate DTOs and endpoints, never hand-write the spec.
- Code and API names are English. User-facing messages come from the module's ICU catalogs in the locale the chain resolved (user, `Accept-Language`, branch, business, `en`); values are raw. See `i18n.md`.

## Sessions and RBAC

- Authentication is an opaque server-side session token; only its hash is stored. Session kinds: `person`, `terminal` (with operator PIN), `guest`.
- Every request resolves the session → principal, tenant and branch (`SessionResolver`, see "Routes and access"); invalid or revoked tokens yield `401` on non-public routes.
- Authorization is role-based per branch and lives in the application layer (use cases / bus, with the access module), never in routes: check the principal's roles for the target branch, not globally.
- Set the tenant for RLS from the session, never from a request parameter.
