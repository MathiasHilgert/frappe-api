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
- [x] T0 Verify Spring Security 7 / Boot 4.1.1 / springdoc versions and APIs from the jars; record here
- [x] T1 Public API + route catalog: startup fails for a route without `@Access`, with two mapped methods or an inconsistent posture; `/v1` prefix
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

### T0 findings (verified from the resolved dependency tree and the sources jars from Maven Central)
- Resolved with `spring-boot-starter-security` and springdoc added: Spring Security 7.1.1 (`spring-boot-security` 4.1.1), Spring Framework (webmvc) 7.0.9, springdoc-openapi 3.1.1 (latest release; its parent is `spring-boot-starter-parent` 4.1.0, swagger-core-jakarta 2.2.55, swagger-ui 5.32.14). `spring-boot-starter-security-test` 4.1.1 exists.
- Security 7: `AuthorizationManager#authorize(Supplier<? extends Authentication>, T)` returns `AuthorizationResult` (the old `check` is gone); `AuthorizationDecision`, `AuthenticatedAuthorizationManager.authenticated()`, `SingleResultAuthorizationManager.permitAll()/denyAll()`. `Authentication#toBuilder()` has a default, so `AbstractAuthenticationToken(Collection)` subclasses need nothing extra.
- `HttpSecurity` DSL (Customizer-only): `csrf`, `sessionManagement`, `httpBasic`, `formLogin`, `logout`, `requestCache`, `anonymous`, `exceptionHandling`, `authorizeHttpRequests` with `dispatcherTypeMatchers`, `requestMatchers(RequestMatcher...)`, `anyRequest().access(AuthorizationManager<RequestAuthorizationContext>)`, `addFilterBefore`.
- Boot's `UserDetailsServiceAutoConfiguration` (`org.springframework.boot.security.autoconfigure`) creates an in-memory user and logs a generated password whenever no `AuthenticationManager`/`AuthenticationProvider`/`UserDetailsService` bean exists: we have none (bearer sessions come from `SessionResolver`), so it is excluded explicitly. Actuator matcher: `org.springframework.boot.security.autoconfigure.actuate.web.servlet.EndpointRequest.to(HealthEndpoint.class)`.
- Spring MVC 7: `HandlerMappingIntrospector` is `@Deprecated(since = "7.0", forRemoval = true)`, so the route for a request is resolved with MVC's own algorithm over `RequestMappingHandlerMapping#getHandlerMethods()`: every `RequestMappingInfo#getMatchingCondition(request)` that matches, best by `RequestMappingInfo#compareTo(other, request)`, a tie is ambiguous (MVC throws `IllegalStateException` there, so the filter denies). Path conditions need the parsed request path (`ServletRequestPathUtils.parseAndCache`, cleared afterwards, as Security's `PathPatternRequestMatcher` does). Known limit: API-versioned mappings (`version` attribute) need MVC's version attribute; `/v1` is our versioning, so none exist.
- `RequestMappingHandlerMapping#setPathPrefixes` / `PathMatchConfigurer#addPathPrefix(String, Predicate<Class<?>>)` + `HandlerTypePredicate.forBasePackage("com.frappe")`: the prefix is part of the registered `RequestMappingInfo`, so matching and springdoc see `/v1/...`.
- springdoc 3.1.1: `org.springdoc.core.customizers.OperationCustomizer#customize(Operation, HandlerMethod)`, `OpenApiCustomizer`; properties `springdoc.swagger-ui.enabled`, `springdoc.api-docs.enabled`.
- Forwarded headers: `server.forward-headers-strategy=native` enables Tomcat's `RemoteIpValve` (X-Forwarded-For/-Proto/-Host); `server.tomcat.remoteip.internal-proxies` accepts a CIDR list or a regex; Boot's default trusts every private range (10/8, 172.16/12, 192.168/16, 100.64/10, 127/8, link-local, fc00::/7, ::1), so it is narrowed to the configured proxy.

### T1 route catalog and `/v1`
- RED `RouteStartupTests` (6, `WebApplicationContextRunner` with WebMvc, DispatcherServlet, message converters and error autoconfiguration + `RouteConfiguration`): compilation failed, `com.frappe.platform.web` (`Access`, `Posture`), `RouteConfiguration` and `InvalidRouteException` missing.
- GREEN 6/6: `startupFailsForARouteWithoutAccessAndNamesTheClass`, `startupFailsForAControllerWithTwoMappedMethodsAndNamesTheClass`, `startupFailsForAPermissionRouteThatNamesNoPermission`, `startupFailsForAPermissionOnAPostureThatChecksNone`, `listsEveryInvalidRouteAtOnce`, `startsWithValidRoutesAndServesThemUnderV1WhileFrameworkControllersKeepTheirPaths` (Boot's `/error` controller is neither checked nor prefixed).
- Code: `Access`, `Posture`, named interface `web` (`package-info`); `RouteCatalog` (checks, one `InvalidRouteException` listing every problem with its fix), `Route`, `ApiPathPrefix` (`addPathPrefix("/v1", HandlerTypePredicate.forBasePackage("com.frappe"))`), `RouteConfiguration`.
- Shared test touched: `RequestTracingTests.ProbeController` now declares `@Access(Posture.PUBLIC)` and is called under `/v1` (it would otherwise fail startup, as intended). `RequestTracingTests` 3/3, `FrappeApiApplicationTests` 1/1, `ModularityTests` 2/2 green; `spotlessCheck javadoc` green.

## Next step
T2.
