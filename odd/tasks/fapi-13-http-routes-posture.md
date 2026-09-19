# FAPI-13 — platform: Serve HTTP routes with a declared access posture

Plane: [FAPI-13](https://app.plane.so/nulled-software/browse/FAPI-13/) (module platform, size M, sensitive: security). Branch: `feat/fapi-13-http-routes-posture`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-13`.

## Objective
Every HTTP route states who may call it, or the application does not start; the posture is enforced by one Spring Security filter chain before any controller code runs. Sessions are resolved later by identity through a hook.

## Decisions
- One route = one class: a `@RestController` (convention: `..infrastructure.web`) with exactly one mapped method, annotated `@Access`. Startup check over `RequestMappingHandlerMapping` for `com.frappe` handler types: a class without `@Access`, with two or more mapped methods, or with an inconsistent posture fails startup and names the class (all problems listed at once).
- Postures (`Posture`): `PUBLIC`, `AUTHENTICATED` (the "self" posture: the use case acts only on the caller's own principal), `PERMISSION` (with `@Access(value = PERMISSION, permission = "...")`), `SYSTEM`.
- Enforcement: Spring Security (`spring-boot-starter-security`), one stateless `SecurityFilterChain`: no session, no cookies, CSRF/form login/basic/logout/request cache off. One `AuthorizationManager` per route built from its posture; requests that match no route are denied (deny by default). Infrastructure endpoints with their own public rule: the error dispatch, `/v3/api-docs/**`, swagger-ui (only served in `local`), `/actuator/health`.
- Tokens only from `Authorization: Bearer <token>` (scheme case-insensitive, RFC 6750 token syntax); cookies and query parameters are never read. An unresolvable token leaves the caller anonymous; the posture decides (PUBLIC still answers, the rest 401 with `WWW-Authenticate: Bearer`).
- `SessionResolver#resolve(String bearerToken)` → `Optional<ResolvedSession>` (principal id, session id, `SessionKind` PERSON/TERMINAL/GUEST); implemented by identity later. Without an implementation no token resolves, so every non-public route answers 401. The session is the Spring Security principal (`@AuthenticationPrincipal ResolvedSession`).
- `PermissionEvaluator` port; the default denies everything (RBAC per branch comes with access). SYSTEM is never reachable over HTTP (always denied).
- `/v1` prefix applied once with `PathMatchConfigurer#addPathPrefix` to `com.frappe` handler types.
- OpenAPI: springdoc-openapi-starter-webmvc-ui (version verified in T0; ticket says 3.1.1); spec at `/v3/api-docs` in every profile, swagger-ui only in `local`. An `OperationCustomizer` adds the bearer requirement and documents 401/403 on non-public routes.
- Client IP from `X-Forwarded-For` through Spring Boot's forwarded-header support (`server.forward-headers-strategy=native`, Tomcat `RemoteIpValve`) trusting only the configured proxy addresses.
- Errors: 401/403 use Spring's defaults for now; ProblemDetail shape and localization are FAPI-14.
- Public API (`com.frappe.platform.web`, a Modulith named interface): `@Access`, `Posture`, `SessionResolver`, `ResolvedSession`, `SessionKind`, `PermissionEvaluator`. Implementation package-private in `com.frappe.platform.infrastructure.web`.
- Standards from `writing-code` (Javadoc with doclint, errors, ECS logs, clean code) and `testing-code` apply. Business metrics: none (HTTP RED metrics are automatic).

## Out of scope
Problem bodies and localization (FAPI-14); session storage and step-up (identity); RBAC (access); CORS; rate limiting.

## TDD
Strict TDD. Runner: `./gradlew test` (MockMvcTester, `WebApplicationContextRunner` startup-failure tests, Testcontainers Postgres + NATS for full contexts, `FRAPPE_TEST_DB=frappe_fapi_13`). RED before each behavior.

## Tasks
- [ ] T0 Verify Spring Security 7 / Boot 4.1.1 / springdoc versions and APIs from the jars; record here
- [ ] T1 Public API + route catalog: startup fails for a route without `@Access`, with two mapped methods or an inconsistent posture; `/v1` prefix
- [ ] T2 Stateless security chain: bearer-only session resolution, posture enforcement (PUBLIC, AUTHENTICATED, PERMISSION, SYSTEM), deny by default, health and error dispatch reachable
- [ ] T3 OpenAPI: spec with bearer requirement and 401/403 on non-public routes; swagger-ui only in `local`
- [ ] T4 Client IP from `X-Forwarded-For`, trusting only the proxy
- [ ] T5 Docs (`writing-code/references/http-api.md` documents `@Access`), `./gradlew spotlessApply check --rerun-tasks` green

## Acceptance (from ticket)
- Route class without `@Access`, or a controller with two mapped methods → startup fails and names the class.
- PUBLIC route without a token → answers normally.
- AUTHENTICATED route with no or an unresolvable bearer token → 401; token in a cookie or query parameter → ignored, 401.
- PERMISSION route with a resolved session and the default evaluator → 403. SYSTEM route → 403 for every session kind.
- `local` profile → swagger-ui served; any other profile → not. In both, `/v3/api-docs` shows the bearer requirement on every non-public route.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_13 ./gradlew spotlessApply check --rerun-tasks`.

## Progress / evidence
(filled per task)

## Next step
T0.
