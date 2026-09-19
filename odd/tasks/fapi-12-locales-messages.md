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
- [ ] T0 Verify ICU4J 78.3 (`LocaleMatcher`, `LocalePriorityList`, `MessageFormat`, `MessagePattern`) and Spring (`AbstractMessageSource`, `LocaleResolver` bean name, Boot's `localeResolver`/`messageSource` conditions, filter dispatcher types) and Modulith `@NamedInterface` from the jars; record here
- [ ] T1 `SupportedLocales`: CLDR matching of a locale and of an `Accept-Language` header, restricted to enabled languages
- [ ] T2 Locale chain: ports, `TenantLocales`, the `LocaleResolver`
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

## Next step
T0.
