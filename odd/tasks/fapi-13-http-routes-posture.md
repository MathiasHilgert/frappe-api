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
- [x] T2 Stateless security chain: bearer-only session resolution, posture enforcement (PUBLIC, AUTHENTICATED, PERMISSION, SYSTEM), deny by default, health and error dispatch reachable
- [x] T3 OpenAPI: spec with bearer requirement and 401/403 on non-public routes; swagger-ui only in `local`
- [x] T4 Client IP from `X-Forwarded-For`, trusting only the proxy
- [x] T5 Docs (`writing-code/references/http-api.md` documents `@Access`), `./gradlew spotlessApply check --rerun-tasks` green

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

### T2 stateless security chain
- Added `spring-boot-starter-security` (+ `spring-boot-starter-security-test`) and the ports `SessionResolver`, `ResolvedSession`, `SessionKind`, `PermissionEvaluator` (declarations only) before any behaviour.
- RED `RouteAccessTests` (full context, MockMvcTester, nested `Routes` test configuration with one route per posture and a test `SessionResolver`) against Boot's default chain: 6 of 17 failed for the expected reasons, e.g. `aPublicRouteAnswersWithoutAToken` expected 200 but was 401, `anAuthenticatedRouteAnswersTheCallerOfAResolvedSession` 401, `aPermissionRouteRefusesARequestWithoutATokenAsUnauthenticated` expected 401 but was 403, `aRequestThatMatchesNoRouteIsRefused` expected 403 but was 401, the 401 carried `WWW-Authenticate: Basic realm="Realm"` instead of `Bearer`. RED `DefaultSessionPortsTests` (2, `WebApplicationContextRunner` + MockMvc with `springSecurity()`): compilation, `SecurityConfiguration` missing.
- GREEN: `SecurityConfiguration` (one stateless chain: CSRF, session, request cache, form login, basic, logout off; `BearerAuthenticationEntryPoint` 401 + `WWW-Authenticate: Bearer`; error dispatch and `EndpointRequest.to(HealthEndpoint)` public; everything else `RouteAuthorizationManager`), `BearerSessionFilter` (exactly one `Authorization` header, case-insensitive `Bearer`, RFC 6750 b64token; unresolved → anonymous, DEBUG line without the token), `SessionAuthentication` (principal = `ResolvedSession`, no credentials), `RouteAuthorizationManager` (one manager per route: PUBLIC permitAll, AUTHENTICATED authenticated, PERMISSION session + `PermissionEvaluator`, SYSTEM denyAll; no route → denied), `RouteCatalog#routeFor` (best match over all annotated mappings with MVC's ordering; ties denied). Defaults: no sessions, no permissions (`ObjectProvider#getIfAvailable`). `RouteAccessTests` 17/17, `DefaultSessionPortsTests` 2/2.
- RED `RouteAccessTests.noPasswordUserIsCreated`: `Expecting empty but was: [InMemoryUserDetailsManager@…]`. GREEN after excluding `UserDetailsServiceAutoConfiguration` in `application.properties`.
- Guard `thePostureIsTheOneOfTheRouteSpringMvcDispatchesTo` (`/test/items/{id}` AUTHENTICATED vs `/test/items/featured` PUBLIC): green at once; mutation check (sorting the matches in reverse) made it fail with `expected: 200 but was: 401`, then reverted.
- Shared test touched: `RequestTracingTests.logLinesOfARequestCarryItsTraceAndSpanIds` failed after the chain existed (`span.id` was a child span): Spring Security's observation wraps the dispatch in its secured-request span, so the log line now carries that span. The assertion now checks the trace id and that the logged span belongs to the server span's trace.
- `FRAPPE_TEST_DB=frappe_fapi_13 ./gradlew spotlessApply check`: BUILD SUCCESSFUL, 194 tests.

### T3 OpenAPI
- RED `OpenApiTests` (local profile) and `OpenApiOutsideLocalProfileTests` (default profile; database credentials given as the `FRAPPE_*` properties a deployment sets, URL from the container): 4/4 failed, `expected: 200 but was: 401` for `/v3/api-docs` and `/swagger-ui/index.html` (no springdoc yet, and deny by default), `expected: 404 but was: 401` outside local.
- GREEN 4/4: `springdoc-openapi-starter-webmvc-ui:3.1.1`; `springdoc.swagger-ui.enabled=false` in `application.properties`, `true` in `application-local.properties`; spec and swagger-ui paths public in the chain; `PostureDocumentation` (`OpenApiCustomizer` adds the `bearer` HTTP scheme, `OperationCustomizer` adds the bearer requirement and 401 on every non-public route, 403 where the posture can refuse a session: PERMISSION, SYSTEM), wired by `OpenApiConfiguration`.
- `FRAPPE_TEST_DB=frappe_fapi_13 ./gradlew spotlessApply check`: BUILD SUCCESSFUL, 198 tests.

### T4 client address
- RED `ClientAddressTests` (real server, RestTestClient over loopback, PUBLIC test route answering `getRemoteAddr()`): `theAddressForwardedByATrustedProxyIsTheClientAddress` and `addressesTheCallerPrependedAreNotTrusted` failed with `expected: "198.51.100.23" but was: "127.0.0.1"`; nested `WhenThePeerIsNoTrustedProxy.theForwardedAddressIsIgnored` (`FRAPPE_TRUSTED_PROXIES=10.0.0.0/8`) passed (headers were ignored altogether).
- GREEN 3/3: `server.forward-headers-strategy=native` (Tomcat `RemoteIpValve`) and `server.tomcat.remoteip.internal-proxies=${FRAPPE_TRUSTED_PROXIES:127.0.0.0/8, ::1/128}`. Mutation check: without the `internal-proxies` line (Boot's default trusts every private range and loopback) the nested test fails, so it guards the narrowing.

### T5 docs and verification
- `writing-code/references/http-api.md`: new "Routes and access" section (one route per class, `@Access` example, posture table with refusal codes, startup checks, bearer-only header, `SessionResolver`/`PermissionEvaluator` defaults, `/v1`, client address, OpenAPI, how tests register routes). README: `/v1`, `/v3/api-docs`, swagger-ui in `local`, `FRAPPE_TRUSTED_PROXIES`.
- Refactor (tests green): `RouteCatalog` groups mappings in `routeMethodsByType` (sorted by class name for a stable failure message); `Route` lost its unused `type` component.
- Verification `FRAPPE_TEST_DB=frappe_fapi_13 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 51 test classes, 201 tests, 0 failures.

### Commits
T0 `ce42b24` (plan `bd34405`), T1 `2e683f6`, T2 `0f277bb`, T3 `11a1dd6`, T4 `93be6f3`, T5 refactor `8730433` and this docs commit.

## Rework after review (human decisions)

### R1 authentication only
- Decision: the HTTP layer authenticates, never authorizes. `PERMISSION`, `SYSTEM`, `Access#permission` and `PermissionEvaluator` (+ default) removed; postures are `PUBLIC` and `AUTHENTICATED`; OpenAPI keeps bearer + 401, no 403. Authorization (RBAC per branch) moves to the application layer (use cases / bus) with the access module in a later ticket; boundary documented in `http-api.md`, `Posture`, `Access`, the `web` package and the security classes.
- Session-kind restriction per route: left out. It is cheap, but deciding which kinds may run an operation is authorization; the use case gets the kind in `ResolvedSession` and decides, so the HTTP layer keeps one concern.
- RED `AccessTest` (unit): `aPostureOnlyStatesWhetherTheCallerMustBeAuthenticated` (`Expecting actual: [PUBLIC, AUTHENTICATED, PERMISSION, SYSTEM] to contain exactly [PUBLIC, AUTHENTICATED]`) and `accessDeclaresAPostureAndNoPermission` (`["value", "permission"]`). GREEN 2/2 after the removal; permission/system tests and routes removed from `RouteStartupTests` (4), `RouteAccessTests` (14), `OpenApiTests` (authenticated route asserts no 403).

### R2 REST semantics for unmatched requests
- Decision: RFC 9110 semantics. Unknown path → 404, known path with another method → 405 + `Allow`, with or without a session; protected route without a valid token → 401 + `WWW-Authenticate: Bearer`. Invariant kept: no controller code runs without its posture satisfied; every route still declares `@Access`.
- RED `RouteAccessTests.anUnknownPathIsNotFoundWithOrWithoutASession` (`expected: 404 but was: 401`), `anUnsupportedMethodOnARouteIsNotAllowedAndNamesTheAllowedOnes` (`expected: 405 but was: 401`); `actuatorEndpointsOtherThanHealthStayClosed` added as a guard (actuator endpoints live in another handler mapping, so without an explicit rule they would now pass through).
- GREEN: `RouteCatalog#match` returns a sealed `RouteMatch` (`ApplicationRoute`, `OtherHandler` for framework controllers and ambiguous matches, `NoHandler`). `RouteAuthorizationManager`: route → posture, other handler → denied (fail closed, documented in the Javadoc), no handler → granted so Spring MVC answers 404/405. The chain denies `EndpointRequest.toAnyEndpoint()` after permitting health. Also the reviewer's cheap fast path: mappings of the exact lookup path first (`RequestMappingInfo#getDirectPaths`), as `AbstractHandlerMethodMapping#lookupHandlerMethod` does. Web tests, `RequestTracingTests`, `OtlpUnavailableTests` green.

## Known behaviour and follow-ups
- Superseded by R2: unknown paths answer 404, unsupported methods 405. CORS preflight (OPTIONS) must bypass the posture when CORS arrives (out of scope).
- 401/403 bodies are Spring Boot's default error JSON (`sendError`), not yet `ProblemDetail`: FAPI-14.
- Spring Security's observations add `spring.security.*` spans (filter chains, authorization, the secured request) and timers per request; the controller's log lines carry the secured-request span. Tag cardinality is bounded.
- Route resolution ignores API-versioned mappings (`version` attribute on a mapping): none exist, `/v1` is the versioning.
- The package convention `..infrastructure.web` for route classes is documented, not enforced at startup (the ticket names two checks).
- Engram mirror `odd/fapi-13-http-routes-posture/tasks`: pending (memory tools not available to this worker).

## Next step
Review and PR (not created here: no push, no Plane change).
