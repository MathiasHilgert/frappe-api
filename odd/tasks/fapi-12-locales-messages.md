# FAPI-12 — platform: Resolve locales and localize messages from day one

Plane: [FAPI-12](https://app.plane.so/nulled-software/browse/FAPI-12/) (module platform, size M, wave 1). Branch: `feat/fapi-12-locales-messages`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-12`.

## Objective
Every response speaks exactly one supported language (en, es, pt), chosen by one locale chain, and every user-facing string comes from hand-translated ICU catalogs whose completeness and syntax `./gradlew check` proves, before any module writes user-facing text.

## Decisions (from the ticket and the brief)
- ICU4J `com.ibm.icu:icu4j:78.3`; `LocaleMatcher` over en, es, pt, so regional variants resolve through CLDR (es-AR → es, pt-BR → pt).
- Chain: authenticated user preference > `Accept-Language` > branch default > business default > en. With a business context, matching is limited to the business's enabled languages; until organization exists, all three are enabled. Anonymous (diner) requests start at `Accept-Language`. The chain always ends in en.
- Ports (public, `com.frappe.platform.i18n`): `UserLocalePreference` (identity implements it) and `TenantLocaleDefaults` (branch default, business default, enabled languages; organization implements it). Platform ships no-op defaults.
- A filter sets `Content-Language` and adds `Vary: Accept-Language` on every response.
- `IcuMessageSource`: a Spring `AbstractMessageSource` backed by ICU MessageFormat (v1 syntax: plural, select), loading per-module catalogs `i18n/<module>/messages_{en,es,pt}.properties`, translated by hand (no TMS, no machine translation).
- Catalog checks in `./gradlew check`: identical key sets per module, every message parses as ICU; violations name module, locale and key. Pseudo-locale en-XA (accented, bracketed English) for tests.
- `writing-code/references/i18n.md`: keys and ICU syntax; the API returns raw values (money as minor units + ISO 4217, instants as RFC 3339 + branch zone; ICU4J formats server-side only for emails and receipts, no Moneta); tenant content in one translation table per entity (origin SOURCE/MACHINE/HUMAN, status DRAFT/NEEDS_REVIEW/APPROVED, `source_digest`, per-field fallback over the chain); tenant content translated by the owner or at their request through the translation module, never automatically, machine output marked to diners.
- No ProblemDetail handling (FAPI-14) and no web-layer conventions (FAPI-13); shared-file edits minimal (parallel FAPI-10, 11, 13, 16).
- Standards from `writing-code`, `testing-code`, `observing-the-api` apply (no business metrics: infrastructure).

## Out of scope
Entity translation tables, the translation module and DeepL; storing preferred, business and branch locales (identity, organization); problem bodies (FAPI-14); email templates (FAPI-15/05).

## TDD
Strict TDD, source: project standard (`testing-code`) and the brief. Runner: `./gradlew test` (Testcontainers Postgres, `FRAPPE_TEST_DB=frappe_fapi_12`; never H2). RED observed before each behavior.

## Tasks
- [x] T0 Verify ICU4J 78.3 (`LocaleMatcher`, `LocalePriorityList`, `MessageFormat`, `MessagePattern`) and Spring (`AbstractMessageSource`, `LocaleResolver` bean name, Boot's `localeResolver`/`messageSource` conditions, filter dispatcher types) and Modulith `@NamedInterface` from the jars; record here — commit `64e5a38`
- [x] T1 `SupportedLocales`: CLDR matching of a locale and of an `Accept-Language` header, restricted to enabled languages — commit `3e54655`
- [x] T2 Locale chain: ports, `TenantLocales`, the `LocaleResolver` — commit `e78fc51`
- [x] T3 `Content-Language` + `Vary: Accept-Language` filter and web wiring — commit `d8ab7ed`
- [x] T4 `IcuMessageSource` over per-module catalogs, plural forms, en-XA pseudo-locale, application `MessageSource` — commit `fea3b31`
- [x] T5 Catalog checks (key parity, ICU syntax) in `./gradlew check`, naming module, locale and key — commit `e5159bb`
- [x] T6 `i18n.md` and skill routing; final verification — commit `d369560`
- [x] R1 Review MAJOR: localization never fails a request (port isolation) — commit `56c11e7`
- [x] R2 Review minors: enabled languages matched through CLDR, CLDR es mappings pinned and documented — commit `e477549`; shipped-catalog guard explained — commit `c0aa055`
- [x] D1 DX: problem detail keys of a module's exceptions accepted by the catalog check — commit `762bd34`
- [x] D2 DX: kernel port `Messages` over Spring's `MessageSourceAccessor`; docs (example, problem details, IDE key safety) — commit `81aa904`
- [x] K1 Review: kernel `com.frappe.platform.i18n` exposes only `java.*` types (ArchUnit rule); Spring/ICU classes moved to `infrastructure.i18n`; ports no longer take the servlet request — commit `db87571`

## Acceptance (from ticket)
- `Accept-Language: es-AR` without a session → `Content-Language: es`, `Vary` includes `Accept-Language`.
- pt-BR → pt. fr, or no header and no tenant defaults → en.
- Stub user preference pt and `Accept-Language: es` → pt.
- No header, branch default es, business default pt → es; without the branch default → pt.
- Enabled languages es and pt and `Accept-Language: en` → the business default.
- An ICU plural message in each catalog → counts 1 and 2 resolve to the right forms in en, es, pt.
- A key missing from one catalog, or invalid ICU syntax → the check fails, naming module, locale and key.
- A test in en-XA → resolved messages are pseudo-localized.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_12 ./gradlew spotlessApply check --rerun-tasks`.

## Progress / evidence

### T0 findings (verified from `icu4j-78.3-sources.jar` (Maven Central), the Gradle cache sources of spring-context / spring-web / spring-webmvc 7.0.9, spring-boot / spring-boot-webmvc / spring-boot-autoconfigure 4.1.1, spring-modulith-api / -core 2.1.1, and a probe run against `icu4j-78.3.jar`)
- `LocaleMatcher.builder().setNoDefaultLocale().addSupportedLocale(..).build()`; `getBestLocaleResult(Locale)` and `getBestMatchResult(Iterable<ULocale>)` return a `Result` whose `getSupportedLocale()` is `null` (index -1) without a good match. With no supported locales at all it returns `null` too (no exception).
- Probe over {en, es, pt}: es-AR → es, pt-BR → pt, pt-PT → pt, es-419 → es, es_AR → es; fr, it, `*`, `zz-ZZ`, en-XA, `Locale.ROOT`, empty → no match. CLDR also maps gl and ca → es (their closest supported language; accepted). Over {es, pt}: en → no match, es-MX → es.
- `LocalePriorityList.add(String)` parses RFC 2616 leniently (`\s*,\s*` items, `;q=`), orders by weight (`es-AR;q=0.8, en;q=0.9` → en first), drops q=0, and throws `IllegalArgumentException` for a weight outside 0..1 and `NumberFormatException` (a subclass) for a non-numeric weight. Garbage tags such as `@@@` parse to nothing. So a malformed header is caught as `IllegalArgumentException` and treated as absent.
- `com.ibm.icu.text.MessageFormat(String, ULocale)` throws `IllegalArgumentException` with the pattern index on invalid syntax (`Bad plural pattern syntax: [at pattern index 11] ...`); `format(Object[])` throws `IllegalArgumentException` for patterns with named arguments. `MessagePattern#hasNamedArguments()` detects them. ICU instances are not thread-safe. Plurals (CLDR): en 1 item / 2 items; es 1 / 2 likewise; pt 0 and 1 are `one`; en-XA uses English rules. Apostrophes: `don't` stays literal, `'{'x'}'` quotes syntax, `''` is one apostrophe.
- `MessagePattern` parts: literal message text is the text after a `MSG_START`, `ARG_LIMIT`, `SKIP_SYNTAX`, `INSERT_CHAR` or `REPLACE_NUMBER` part up to the next part; text after `MSG_LIMIT` or inside an argument is syntax. The en-XA transform rewrites only those regions, so placeholders and selectors survive.
- Spring `AbstractMessageSource#getMessage(..)` variants are `final`; `getMessageInternal(code, args, locale)` is the protected hook (it falls back to common messages and the parent), `resolveCode` must return a `java.text.MessageFormat`. `IcuMessageSource` overrides `getMessageInternal` for its catalogs (always formatting through ICU, also without arguments, so apostrophes behave the same) and returns `null` from `resolveCode`. A `null` locale becomes `Locale.getDefault()` in the base class, so the override maps `null` to en itself.
- Boot 4.1.1: `MessageSourceAutoConfiguration` backs off when a bean named `messageSource` exists; `WebMvcAutoConfiguration` creates `localeResolver` only `@ConditionalOnMissingBean(name = "localeResolver")`; `DispatcherServlet` looks the resolver up by that name.
- `AbstractFilterRegistrationBean#determineDispatcherTypes`: a filter bean that is a `OncePerRequestFilter` is registered for all dispatcher types; `OncePerRequestFilter` skips ERROR dispatches by default.
- Modulith 2.1.1: `NamedInterfaces.discoverNamedInterfaces` keeps the module's unnamed (root package) interface and adds packages annotated `@NamedInterface` (package local name as default name), so `com.frappe.platform.i18n` becomes the `i18n` interface without hiding the kernel.

### Design (T0)
- Public (`com.frappe.platform.i18n`, `@NamedInterface`): `SupportedLocales` (en, es, pt; matching restricted to enabled languages), `UserLocalePreference` and `TenantLocaleDefaults` (take the `HttpServletRequest`: identity reads the session from it, organization the business and branch of the session or of an anonymous diner's path, independent of filter order), `TenantLocales` (enabled languages, business default, optional branch default), `IcuMessageSource`.
- Internal (`com.frappe.platform.infrastructure.i18n`): the chain `LocaleResolver` (resolves once per request, cached in a request attribute), the `Content-Language` / `Vary` filter, and the configuration (no-op ports until identity and organization provide theirs).
- Catalogs load from `classpath*:i18n/*/messages_*.properties` (UTF-8); keys start with `<module>.`, so modules never collide in the flat `MessageSource`. Invalid catalogs stop startup with every violation listed, as invalid business metrics do; the same check runs as a test in `./gradlew check`.
- Arguments are numbered (`{0}`), because `MessageSource` passes an `Object[]`; named arguments are a catalog violation.

### T1 SupportedLocales (commit `3e54655`)
- RED `SupportedLocalesTest` (compilation): `cannot find symbol ... SupportedLocales` (14 errors).
- GREEN 25/25: `all()` = en, es, pt, `FALLBACK` en; `match(Locale)` via ICU `LocaleMatcher` without default locale (es-AR, es-419 → es; pt-BR, pt-PT → pt; en-GB → en; fr, it, und, en-XA → empty); `matchAcceptLanguage` honours weights and `q=0`; an empty, unsupported or malformed header (`en;q=2`, `es;q=abc`) matches nothing (DEBUG log with the raw header); `restrictedTo` narrows to enabled languages.
- `build.gradle.kts`: `com.ibm.icu:icu4j:78.3`; `com.frappe.platform.i18n` is a `@NamedInterface`. `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T2 locale chain (commit `e78fc51`)
- RED `LocaleChainResolverTest` (compilation): `cannot find symbol` for `TenantLocaleDefaults`, `TenantLocales`, `UserLocalePreference`, `LocaleChainResolver`.
- GREEN 13/13: anonymous es-AR → es, pt-BR → pt, fr → en; no header and no tenant → en; preference pt + `Accept-Language: es` → pt; no header, branch es + business pt → es, without branch → pt; enabled {es, pt} + en → business default pt; a preference outside the enabled languages falls through to `Accept-Language`; no enabled default → en; several `Accept-Language` lines form one list; ports asked once per request; `setLocale` unsupported.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T3 headers and wiring (commit `d8ab7ed`)
- RED `ContentLanguageFilterTest` (compilation); GREEN 3/3 (`Content-Language` = BCP 47 tag; `Vary: Accept-Language` added once, other values kept, case-insensitive).
- RED `LocaleResolutionIntegrationTests` (real server, Testcontainers), 4/4 failing: `expected: "es" but was: "es-AR"` (Boot's `AcceptHeaderLocaleResolver`), `Content-Language expected:<[en]> but was:<[]>`, `expected: "pt" but was: null` on the 404.
- GREEN 4/4 with `I18nConfiguration` (`localeResolver` bean = chain; no-op ports via `ObjectProvider.getIfAvailable`; filter at `HIGHEST_PRECEDENCE + 10`): es-AR → `Content-Language: es` + `Vary`; no header → en; stub preference pt beats es; 404 and 500 carry `Content-Language: pt` and one `Vary`.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T4 IcuMessageSource (commit `fea3b31`)
- RED `IcuMessageSourceTest`, `PseudoLocalizationTest` (compilation). GREEN `IcuMessageSourceTest` 11/11 over `src/test/resources/i18n/sample`: plural 1 and 2 in en (item/items), es (artículo/artículos), pt (item/itens); es-AR reads Spanish; fr and `null` read English; messages without arguments go through ICU; en-XA → `[Ĥéĺĺó, Ana!]`, `[2 íŧéɱš]`; unknown code → `NoSuchMessageException` or the given default.
- `PseudoLocalizationTest` first run 4/6: two expected strings in the test used other accents (Ŝ, Û) than the table (Š, Ú); test data corrected, 6/6.
- RED `I18nIntegrationTests` (renamed from `LocaleResolutionIntegrationTests`), 2 new tests: `messageSource` was `DelegatingMessageSource`, probe 500. First GREEN attempt: context failed, `Could not generate CGLIB subclass of class ...IcuMessageSource` (Modulith observability proxies exposed types); fixed with `@Role(ROLE_INFRASTRUCTURE)` on the bean (skipped by `ModuleObservabilityBeanPostProcessor#isInfrastructureBean`, so no span per message either). GREEN 6/6 (`Accept-Language: pt-BR` → `2 itens`).
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T5 catalog checks (commit `e5159bb`)
- RED `MessageCatalogCheckTest`, `ShippedMessageCatalogsTest` (compilation). GREEN 8/8 + 1/1: missing key → `i18n/orders/messages_es.properties: key 'orders.placed' is missing (present in en, pt); ...`; missing catalog reports each key; invalid ICU, named arguments, keys outside `<module>.`, unsupported locale files are violations; `IcuMessageSource.load` of `i18n-fixtures/broken` throws `InvalidMessageCatalogsException` listing all (startup fails).
- Gate proof: with `sample.greeting` removed and `sample.items` broken in the Spanish test catalog, `ShippedMessageCatalogsTest` failed naming both keys (`is missing (present in en, pt)`, `Bad plural pattern syntax: [at pattern index 11]`); catalog restored.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T6 docs (commit `d369560`)
- `writing-code/references/i18n.md` (chain, catalogs, ICU syntax, checks, en-XA, raw values, tenant content); `SKILL.md` rule + routing row; `http-api.md` i18n line.
- `./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 52 classes, 240 tests, 0 failures.

### Review (one MAJOR, minors)
- R1 MAJOR (commit `56c11e7`). RED `LocaleChainResolverTest.aFailingUserPreferenceCountsAsNoPreferenceAndWarnsOnce` and `aFailingTenantLookupEnablesEveryLanguageAndEndsInEnglish`: `IllegalStateException: session store unavailable` / `organization unavailable` escaped the resolver. GREEN 15/15: each port call is an isolation boundary; a failure counts as no value for its link (a failing tenant lookup enables every language), one WARN per failing link per request (the result is cached per request) with `frappe.locale_link` (`user_preference` / `tenant_defaults`) and the cause. RED `FailingLocalePortsIntegrationTests.healthStaysUpAndLocalizedWhenTheLocalePortsFail` against the previous resolver: `Status expected:<200 OK> but was:<500 INTERNAL_SERVER_ERROR>`; GREEN with the fix (`/actuator/health` 200, `Content-Language: es`). `errors.md` adds the localization isolation boundary; `i18n.md` states the rule.
- R2 (commit `e477549`). RED `SupportedLocalesTest.restrictedToRegionalEnabledLanguagesKeepsTheirSupportedLanguage`: `Expecting actual: [] to contain exactly: [es, pt]` for enabled pt-BR, es-AR. GREEN 28/28: enabled locales are matched through CLDR. Rows `ca, es` and `gl, es` pin CLDR (green at once: guards, no RED); a probe confirmed ca, gl, eu, gn, qu → es; documented in `i18n.md`.
- R3 (commit `c0aa055`). `ShippedMessageCatalogsTest` keeps `isNotEmpty()` with a comment: without it a broken location pattern would check nothing and pass.
- Note: T1–T6 evidence was first written with `sd` substitutions that silently did not match, so only the checkboxes had changed; this section restores it.

### Developer experience (human direction: libraries inside adapters, modules depend on kernel types)
- Research outcome (coordinator): no maintained Spring library for type-safe message bundles; the improvements come from Spring itself. Verified in `spring-context-7.0.9-sources.jar`: `MessageSourceAccessor` resolves the locale from `LocaleContextHolder` but has **no varargs overloads** (`getMessage(String, Object[])`), and `getMessage(String, String)` treats the second argument as a default message, so `accessor.getMessage("x", name)` would silently format without arguments. Hence the kernel port instead of exposing the accessor.
- D1 (commit `762bd34`): Spring's `ErrorResponse#updateAndGetBody` resolves `problemDetail.title.<FQCN>` and `problemDetail.<FQCN>[.<suffix>]` (verified in `spring-web-7.0.9-sources.jar`). Rule: such keys are accepted when the exception is in `com.frappe.<module>.`; platform may also localize framework exceptions (outside `com.frappe.`); `problemDetail.type.*` is rejected (types are stable URIs). RED `MessageCatalogCheckTest`: 4 failing (the three new tests and the reworded namespace message); GREEN 11/11.
- D2 (commit `81aa904`): `com.frappe.platform.i18n.Messages` (`get(key, args...)` in the request locale, `get(locale, key, args...)` outside a request), implemented by `infrastructure.i18n.MessageSourceMessages` over `MessageSourceAccessor`; an unknown key throws `UnknownMessageKeyException` naming key, locale and the module's catalog files. RED `MessageSourceMessagesTest` and `I18nIntegrationTests` (compilation: `Messages` missing); first GREEN run 3/4: the unknown-key message did not name `i18n/sample/messages_{en,es,pt}.properties` (derived from the key's module prefix afterwards); GREEN 4/4 and 6/6 (the HTTP probe now calls `messages.get("sample.items", count)` → `2 itens` for pt-BR).
- `i18n.md`: modules import only kernel types; `Messages` example; "Errors (problem details)" section; key safety via the catalog check and IntelliJ's Resource Bundle Editor, no codegen (IDE behaviour described from IntelliJ documentation, not exercised in this environment).

### Kernel decoupling (commit `db87571`)
- RED `I18nKernelDependenciesTest.theKernelDependsOnlyOnJavaAndItself` (ArchUnit 1.4.2: `classes in com.frappe.platform.i18n` except `package-info` `should only depend on classes in java.., com.frappe.platform.i18n`): violated 54 times (Spring, ICU4J, SLF4J, jakarta.servlet). GREEN after the move.
- Kernel now: `Messages`, `SupportedLocales` (constants, `all()`, `PSEUDO` = en-XA), `UserLocalePreference`, `TenantLocaleDefaults`, `TenantLocales`, `package-info` (its `@NamedInterface` is Modulith metadata, excluded from the rule).
- Moved to `infrastructure.i18n` (package-private): `IcuMessageSource`, `LocaleMatching` (the ICU matching formerly in `SupportedLocales`), `PseudoLocalization`, `MessageCatalog(s)`, `MessageCatalogCheck`, `CatalogViolation`, both catalog exceptions; tests moved with them (`SupportedLocalesTest` split into a kernel test and `LocaleMatchingTest`).
- Ports without `HttpServletRequest`: `preferredLocale()` and `current()`. Implementations read the current request through `RequestContextHolder`; the `Content-Language` filter now runs at `REQUEST_WRAPPER_FILTER_MAX_ORDER - 104`, right after Boot's `RequestContextFilter` (-105, verified in `spring-boot-servlet-4.1.1-sources.jar`) and before security (-100). RED: `I18nIntegrationTests.theSignedInUsersPreferenceWinsOverAcceptLanguage` with the stub port reading `RequestContextHolder` and the old order: `Content-Language expected:<[pt]> but was:<[es]>` (no thread-bound request, isolated as "no preference"); GREEN with the new order. All i18n tests and `ModularityTests` green.
- `i18n.md`: kernel types only, ports read `RequestContextHolder`, `SupportedLocales.PSEUDO`.

### Rebase on main (FAPI-13 routes, commit `d27c071`)
- Rebased on `origin/main` `e2b78f7` (FAPI-13: declared routes, Spring Security chain). Conflict in `errors.md` resolved by keeping FAPI-13's `HandlerMappingGuard` entry and the localization boundary ("beyond those listed here").
- After the rebase `I18nIntegrationTests` failed to start: `InvalidRouteException: Invalid HTTP routes` (the probe controllers were nested in a test outside `..infrastructure.web` and had no `@Access`). The probes are now public routes in `src/test/java/com/frappe/platform/infrastructure/web/LocaleProbeRoutes.java` (one class per route, `@Access(Posture.PUBLIC)`, served under `/v1`), following FAPI-13's `ProbeRoutes`. All i18n tests green, including the stub user port reading `RequestContextHolder` with the security chain in place (the locale filter at -104 runs before security at -100).
- Verification `FRAPPE_TEST_DB=frappe_fapi_12 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 66 classes, 309 tests, 0 failures, 0 skipped.

## PR summary
- Locale chain (user preference > `Accept-Language` > branch > business > en) with ICU/CLDR matching, restricted to enabled languages; ports for identity and organization; a failing port never fails a request.
- `Content-Language` and `Vary: Accept-Language` on every response, errors included.
- `Messages` kernel port: `messages.get("order.items", 2)`, request locale implicit; ICU plural/select; en-XA pseudo-locale for tests. The kernel `com.frappe.platform.i18n` is plain Java (ArchUnit-enforced); Spring and ICU4J live in `infrastructure.i18n`.
- Per-module hand-translated catalogs `i18n/<module>/messages_{en,es,pt}.properties`, checked in `./gradlew check` and at startup (key parity, ICU syntax, numbered arguments, namespace incl. Spring problem detail keys).
- Conventions in `writing-code/references/i18n.md` (raw API values, problem details, tenant content translations); isolation boundary in `errors.md`.

## Follow-ups / open questions
- en-XA is a message-source locale for tests only; the resolver never selects it from `Accept-Language`. Exposing it over HTTP (for client QA) would be a separate, opt-in decision.
- `i18n.md` leaves open which translation statuses diners see (for example only `APPROVED`); the translation module ticket should settle it.
- The only catalogs are the test catalogs (`src/test/resources/i18n/sample`); platform has no user-facing text yet. FAPI-14 adds the first real ones.

## Next step
All tasks and review items are done and verified; next is the PR (not created here: no push, no PR, no Plane change). Final verification after review: `FRAPPE_TEST_DB=frappe_fapi_12 ./gradlew spotlessApply check --rerun-tasks` BUILD SUCCESSFUL, 53 classes, 246 tests, 0 failures, 0 skipped. After the DX changes: 54 classes, 253 tests; after the kernel decoupling: 56 classes, 255 tests, 0 failures, 0 skipped.
