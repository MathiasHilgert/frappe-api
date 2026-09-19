# HTTP API

Package: `com.frappe.<module>.infrastructure.web`.

## Routes and access

One route is one class: a `@RestController` in `..infrastructure.web` with exactly one mapped method and an `@Access` posture (`com.frappe.platform.web`). That is the whole declaration:

```java
@RestController
@Access(value = Posture.PERMISSION, permission = "tabs.close")
class CloseTabRoute {

    @PostMapping("/tabs/{tabId}/close")
    ResponseEntity<Void> close(@PathVariable UUID tabId, @AuthenticationPrincipal ResolvedSession caller) { … }
}
```

| Posture | Who may call | Refused with |
| --- | --- | --- |
| `PUBLIC` | anyone, with or without a token | never |
| `AUTHENTICATED` | any resolved session; the use case acts only on the caller's own principal ("self") | 401 |
| `PERMISSION` | a resolved session the `PermissionEvaluator` grants `permission` | 401 without a session, 403 |
| `SYSTEM` | nobody over HTTP (internal callers only) | 401 without a session, 403 |

- Startup fails, naming the class and the fix, for a route without `@Access`, with two or more mapped methods, or with a permission on a posture that checks none (or `PERMISSION` without one). Only `com.frappe` controllers are routes; framework controllers are not checked.
- The posture is enforced by one stateless Spring Security chain before any controller code runs (no session, cookies, CSRF or login form). A request no route serves is refused (deny by default); the health endpoint, error dispatches and the OpenAPI paths are the only other public paths.
- The token comes from `Authorization: Bearer <token>` only; cookies and query parameters are never read. `SessionResolver` (identity) turns it into a `ResolvedSession` (principal id, session id, `SessionKind` `PERSON`/`TERMINAL`/`GUEST`), read with `@AuthenticationPrincipal ResolvedSession`. A missing or unresolvable token leaves the caller anonymous: public routes still answer, the rest 401 with `WWW-Authenticate: Bearer`. Until identity implements the resolver, no token resolves; until access implements `PermissionEvaluator`, every permission is refused.
- `/v1` is added once for all `com.frappe` controllers: map `/tabs/{tabId}/close`, serve `/v1/tabs/{tabId}/close`.
- The client address is `HttpServletRequest#getRemoteAddr()`: Tomcat takes it from `X-Forwarded-For` only when the connection comes from a trusted proxy (`FRAPPE_TRUSTED_PROXIES`).
- The OpenAPI spec (`/v3/api-docs`, every profile) marks every non-public operation with the `bearer` scheme and documents 401 (and 403 for `PERMISSION`/`SYSTEM`); swagger-ui is served in `local` only.
- Tests register routes as `@Bean`s of a nested `@TestConfiguration` and call them with `MockMvcTester` and an `Authorization: Bearer` header resolved by a test `SessionResolver` bean.

## Endpoints

- REST under `/v1`, plural nouns, kebab-case paths: `POST /v1/sessions`, `DELETE /v1/sessions/current`.
- Controllers translate request DTO (`record`) → command/query, call the bus, map `Result` → response. No business logic.
- `201 Created` with `Location` for creation, `204` for no-body success.
- Paginate collection endpoints (limit + cursor or page); never return unbounded lists.

## Validation

- Jakarta Bean Validation on request DTOs for shape (required, length, format).
- Business rules are validated in the domain and come back as `Result` failures.

## Errors (RFC 9457)

- Every error is a `ProblemDetail`. Map domain errors in one place per module (`@RestControllerAdvice` or a mapper).
- `type` is a stable URI per error (`https://frappe.app/problems/tab-already-closed`), `title` and `detail` localized, extra fields as properties.
- Typical status: validation `400`, unauthenticated `401`, forbidden `403`, missing `404`, rule violation `409`/`422`, optimistic lock `409`.

## OpenAPI and i18n

- OpenAPI is generated from code; annotate DTOs and endpoints, never hand-write the spec.
- Code and API names are English. User-facing messages come from `messages*.properties`, resolved by `Accept-Language`.

## Sessions and RBAC

- Authentication is an opaque server-side session token; only its hash is stored. Session kinds: `person`, `terminal` (with operator PIN), `guest`.
- Every request resolves the session → principal, tenant and branch (`SessionResolver`, see "Routes and access"); invalid or revoked tokens yield `401` on non-public routes.
- Authorization is role-based per branch: check the principal's roles for the target branch, not globally.
- Set the tenant for RLS from the session, never from a request parameter.
