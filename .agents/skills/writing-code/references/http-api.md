# HTTP API

Package: `com.frappe.<module>.internal.infrastructure.web`.

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
- Every request resolves the session → principal, tenant and branch; invalid or revoked tokens yield `401`.
- Authorization is role-based per branch: check the principal's roles for the target branch, not globally.
- Set the tenant for RLS from the session, never from a request parameter.
