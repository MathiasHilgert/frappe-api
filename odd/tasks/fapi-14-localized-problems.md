# FAPI-14 — platform: Answer every failure as a localized problem

Plane: [FAPI-14](https://app.plane.so/nulled-software/browse/FAPI-14/) (module platform, size M). Branch: `feat/fapi-14-localized-problems`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-14`.

## Objective
Every failure reaches the client in one RFC 9457 shape (`application/problem+json`): a stable `type` URI and `code`, `params`, `traceId`, and a `title`/`detail` localized in the resolved locale (en/es/pt). Problem bodies never carry internals.

## Decisions (supersede the ticket where they differ)
- HTTP authenticates only (PUBLIC/AUTHENTICATED): 401 for a missing or invalid token; standard 404 and 405 (with `Allow`); no permission 403s from HTTP (the fail-closed 403 for a caller with a session on a framework path keeps the problem shape).
- Human standing rule: infrastructure failures are never visible to clients (no technology or provider names, exception messages, classes, SQL or stack traces). Any unexpected exception → generic 500 problem (`internal-error`, "An unexpected error occurred", localized), carrying only type, code, traceId (plus RFC fields title/status/detail/instance); logged once at ERROR with ECS fields and counted in a low-cardinality metric.
- Business failures (`Result` errors) map to specific 4xx problems through one `ProblemMapper<E>` bean per failure type: stable `type`, `code`, `params`; validation adds `errors[]` (JSON pointer, code, params, detail).
- Boot's default error JSON for 401/404/405/400 is replaced by `ProblemDetail`, `Content-Language` set.
- Spring-standard mechanisms first (`ResponseEntityExceptionHandler`, `ErrorResponse`, `ProblemDetail`, `MessageSource`, `ErrorController`, Spring Security's delegation to `HandlerExceptionResolver`); libraries only in infrastructure; kernel technology-free (ArchUnit); constructor injection; no static state.

## Out of scope
Module-specific error catalogs; Retry-After and rate-limit headers.

## TDD
Strict TDD. Mode source: project standard (`testing-code`, CLAUDE.md). Runner: `./gradlew test` (MockMvcTester, Testcontainers Postgres + NATS, `FRAPPE_TEST_DB=frappe_fapi_14`). RED before each behavior.

## Tasks
- [x] T0 Verify Spring 7.0.9 / Boot 4.1.1 / Security 7.1.1 APIs from sources; record here
- [x] T1 Kernel contract: `Problem`, `ProblemMapper`, `RequestRefusedException`, `Result#orElseThrow`
- [ ] T2 Framework problems: 404, 405 (+`Allow`), malformed body 400, validation 400 with `errors[]`; localized, type/code/traceId, `Content-Language`
- [ ] T3 401 (and the fail-closed 403) from the security chain as problems
- [ ] T4 Unexpected failures: generic localized 500 per infrastructure failure kind (database down, SQL error, provider, NATS, Valkey, failing filter); one ERROR log + metric
- [ ] T5 `Result` failures through the `ProblemMapper` registry: mapped status/type/code/params, localized; unmapped → 500; duplicate mapper → startup fails
- [ ] T6 Docs (`writing-code/references/errors.md`, `http-api.md`, `i18n.md`), `./gradlew spotlessApply check --rerun-tasks` green

## Acceptance (from ticket, adjusted)
- No session on an authenticated route → 401 `application/problem+json` with type, code, title, status, traceId.
- `Accept-Language: es` → code and params unchanged, title and detail Spanish; `xx` → English.
- Invalid body → 400 with one `errors` entry per violated field, each with a JSON pointer.
- A `Result` failure with a mapper → the mapped status, type and localized text.
- A route that throws → 500, the body has no exception message, class name, SQL, technology name or stack trace (one test per infrastructure failure kind, en/es/pt).
- The platform problem catalogs have the same keys in en/es/pt (existing catalog check).

## Checks
`FRAPPE_TEST_DB=frappe_fapi_14 ./gradlew spotlessApply check --rerun-tasks`.

## Progress / evidence

### T0 findings (sources jars of the resolved versions: spring-web/webmvc 7.0.9, spring-boot-webmvc 4.1.1; Spring Security 7.1.1 binary jar)
- Boot: `spring.mvc.problemdetails.enabled` only registers `ProblemDetailsExceptionHandler` (an empty `ResponseEntityExceptionHandler` `@ControllerAdvice`), `@ConditionalOnMissingBean(ResponseEntityExceptionHandler.class)`, so our own advice replaces it. Error dispatches go to `BasicErrorController` (`ErrorMvcAutoConfiguration`, `@ConditionalOnMissingBean(ErrorController.class)`), whose body is Boot's map JSON, not a problem; `org.springframework.boot.webmvc.error.ErrorController` (marker) and `ErrorAttributes#getError(WebRequest)` are the supported replacement points.
- `ResponseEntityExceptionHandler` (7.0.9): one `@ExceptionHandler` for the MVC exceptions; every path ends in `handleExceptionInternal(ex, body, headers, status, request)` (body from `ErrorResponse#updateAndGetBody(messageSource, LocaleContextHolder.getLocale())` or `createProblemDetail`), then `createResponseEntity`. It implements `MessageSourceAware`. Headers of `ErrorResponse` exceptions (405's `Allow`) are passed through.
- `ErrorResponse#updateAndGetBody` resolves `problemDetail.type.<FQCN>`, `problemDetail.<FQCN>` (with `getDetailMessageArguments`) and `problemDetail.title.<FQCN>`; `ErrorResponse.builder(ex, status, detail)` takes explicit `titleMessageCode`/`detailMessageCode`/`detailMessageArguments`. The code is per concrete class (`MethodArgumentTypeMismatchException`, `MissingRequestHeaderException`, … each their own), and a missing code silently keeps Spring's English default.
- Spring MVC 7 throws `NoHandlerFoundException` (404) when no handler matches (`throwExceptionIfNoHandlerFound` defaults to true); 405 is `HttpRequestMethodNotSupportedException` with `Allow` in its headers; a malformed body `HttpMessageNotReadableException`; `@Valid @RequestBody` `MethodArgumentNotValidException` (`FieldError#unwrap(ConstraintViolation.class)` gives the constraint's attributes).
- `ExceptionHandlerExceptionResolver` applies `@ControllerAdvice` without selectors to a `null` handler (`AbstractHandlerMethodExceptionResolver#shouldApplyTo`, `HandlerTypePredicate#test(null)` is true without selectors), so Spring Security's entry point / access-denied handler can delegate to the `handlerExceptionResolver` bean (the pattern Spring Security documents).
- `JacksonJsonHttpMessageConverter` registers `ProblemDetailJacksonMixin` (properties become top-level members) and writes `ProblemDetail` as `application/problem+json`; `HttpEntityMethodProcessor` sets `instance` to the request path when unset.
- Locale: the `DispatcherServlet` sets `LocaleContextHolder` from our `LocaleResolver`, but the security chain runs before it (only `RequestContextFilter`'s `Accept-Language` locale), so problem text resolves the locale through the `LocaleResolver` bean (cached per request by `LocaleChainResolver`).

### T1 kernel contract
- RED (compilation): `ProblemTest`, `RequestRefusedExceptionTest` (`Problem`, `RequestRefusedException` missing) and `ResultTest.orElseThrowReturnsTheValueOfASuccess` / `orElseThrowThrowsTheExceptionMadeFromTheError` (`orElseThrow` missing).
- GREEN: `ProblemTest` 14/14 (params in order and unmodifiable, status only 4xx, kebab-case slug, key required, named non-null params), `RequestRefusedExceptionTest` 2/2 (message names only the failure type), `ResultTest` 10/10. `WebKernelDependenciesTest` (guard, green at once): `com.frappe.platform.web` depends on the JDK and the kernel only.
- Code: `com.frappe.platform.web.Problem` (record: status, slug, messageKey, ordered params; `of`, `with`, `arguments`), `ProblemMapper<E>` (`failureType`, `problemOf`), `RequestRefusedException` (public, carries the failure), `Result#orElseThrow`.
