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
- [x] T0 Verify ICU4J 78.3 (`LocaleMatcher`, `LocalePriorityList`, `MessageFormat`, `MessagePattern`) and Spring (`AbstractMessageSource`, `LocaleResolver` bean name, Boot's `localeResolver`/`messageSource` conditions, filter dispatcher types) and Modulith `@NamedInterface` from the jars; record here
- [x] T1 `SupportedLocales`: CLDR matching of a locale and of an `Accept-Language` header, restricted to enabled languages
- [x] T2 Locale chain: ports, `TenantLocales`, the `LocaleResolver`
- [ ] T3 `Content-Language` + `Vary: Accept-Language` filter and web wiring
- [ ] T4 `IcuMessageSource` over per-module catalogs, plural forms, en-XA pseudo-locale, application `MessageSource`
- [ ] T5 Catalog checks (key parity, ICU syntax) in `./gradlew check`, naming module, locale and key
- [ ] T6 `i18n.md` and skill routing; final verification

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

## Next step
T1.
