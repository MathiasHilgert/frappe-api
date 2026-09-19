# FAPI-34 — Translate text on demand through a machine translation port

Plane: FAPI-34. Branch: `feat/fapi-34-machine-translation`. Worktree:
`~/Projects/nulled/frappe-api-worktrees/FAPI-34`.

## Objective

Give modules a kernel port to translate text on demand (`en`, `es`, `pt`)
without depending on a provider, backed by a DeepL adapter, so catalog/menu
(later tickets) can request machine translations and store them with
`origin=MACHINE`.

## Why

Owners will translate menus themselves or ask us to machine-translate them.
Before catalog/menu exist, the platform needs the port so modules never
couple to DeepL directly, and so the app starts without a DeepL key (local
dev, CI).

## TDD mode

Strict TDD (RED → GREEN → REFACTOR), source: ticket instruction (repo default
is also strict per `AGENTS.md`/`testing-code`). Runner: `./gradlew test`
(`FRAPPE_TEST_DB=frappe_fapi_34 ./gradlew spotlessApply check --rerun-tasks`
for the full gate). No Testcontainers Postgres/NATS needed for this ticket —
translation has no persistence yet; contract tests stub DeepL's HTTP API with
WireMock (`org.wiremock:wiremock-standalone`).

## T0 — Verify DeepL Java SDK APIs (done, see notes)

Verified from `com.deepl.api:deepl-java:1.17.0` sources (downloaded sources
jar from Maven Central, latest release on `maven-metadata.xml`):

- `DeepLClient(String authKey, DeepLClientOptions options)`; `DeepLClientOptions extends TranslatorOptions`.
- Batch translate: `Translator#translateText(List<String> texts, String sourceLang, String targetLang, TextTranslationOptions options)` → `List<TextResult>`; `TextResult#getText()/getDetectedSourceLanguage()/getBilledCharacters()`.
- `TextTranslationOptions#setFormality(Formality)`, `#setGlossaryId(String)`.
- `TranslatorOptions#setServerUrl(String)` (test override), `#setTimeout(Duration)` (default 10s), `#setMaxRetries(int)` (default 5).
- Exceptions: `DeepLException` (checked base) → `AuthorizationException` (invalid key), `QuotaExceededException`, `TooManyRequestsException`, `ConnectionException` (timeouts/transport, `getShouldRetry()`), `NotFoundException`/`GlossaryNotFoundException`. All checked, so the adapter must catch them explicitly (no `catch (Exception)`).
- Glossaries (v3, multilingual): `DeepLClient#createMultilingualGlossary(name, List<MultilingualGlossaryDictionaryEntries>)`, `#listMultilingualGlossaries()`, `MultilingualGlossaryInfo#getGlossaryId()/getName()`.

Decision: set `maxRetries(0)` in the adapter — the SDK's own retry loop would
silently extend a call past our bounded timeout and violates "nothing is
retried silently" from the acceptance criteria; callers see one bounded
attempt and one dedicated exception.

## Tasks

- [x] T0 verify SDK APIs (above)
- [x] T1 kernel port: `MachineTranslator`, `TranslationRequest`, `Translation`,
      `TranslationFormality`, `MachineTranslationUnavailableException`,
      `MachineTranslationQuotaExceededException` in `com.frappe.platform.i18n`
      (plain Java only; `I18nKernelDependenciesTest` covers it, no new ArchUnit
      test needed).
- [x] T2 DeepL adapter `DeepLMachineTranslator` in
      `com.frappe.platform.infrastructure.i18n`, contract-tested against a
      WireMock stub server via `TranslatorOptions#setServerUrl`.
- [x] T3 missing-key / unavailable behavior + startup does not fail.
- [x] T4 telemetry: `translation.request` observation, `translation.characters`
      counter, tags target language + outcome (low cardinality).
- [x] T5 fake adapter `FakeMachineTranslator` for tests / local work without a key.
- [x] T6 glossary support: `DeepLMachineTranslator` forwards a caller-supplied
      glossary reference; adapter-level `DeepLGlossaries` creates-or-reuses one
      glossary per business (contract-tested).
- [x] T7 wiring: `DeepLTranslationProperties` (`frappe.translation.deepl.*`),
      `TranslationConfiguration`, `application.properties` entry for
      `FRAPPE_DEEPL_API_KEY` (optional, empty default).
- [x] T8 docs: update `writing-code/references/i18n.md` with how a module
      requests a translation and stores it with `origin=MACHINE`.
- [x] T9 `./gradlew spotlessApply check` green.

## Verification evidence

`FRAPPE_TEST_DB=frappe_fapi_34 ./gradlew spotlessApply check --rerun-tasks` → BUILD SUCCESSFUL. See report to caller
for RED/GREEN test names.

## Standing rules applied mid-ticket

- DeepL key is Free plan (`:fx` suffix); the SDK's `Translator` constructor already selects `api-free.deepl.com`
  from that suffix when `serverUrl` is not overridden (verified in `Translator.java`), so production wiring needs no
  extra branching — only tests override `serverUrl` to point at WireMock.
- `docs/secrets.md` landed on `main` (PR #19) mid-ticket; branch rebased onto `origin/main`, `FRAPPE_DEEPL_API_KEY`
  added to its "Secrets the application reads" table.
- Provider failures are sanitized at the adapter boundary: `DeepLMachineTranslator`/`DeepLGlossaries` map every DeepL
  exception to `MachineTranslationUnavailableException`/`MachineTranslationQuotaExceededException` with messages that
  never name DeepL, log the original failure once at ERROR with `frappe.target_language`/`frappe.translation_outcome`,
  and the `translation.request` observation's `outcome` tag counts it — the "no key configured" fast path is not
  logged (expected condition, not a failure).

## Decisions

- Kernel exceptions: only two dedicated types (`MachineTranslationUnavailableException`,
  `MachineTranslationQuotaExceededException`); auth failure, timeout and
  connection failure all map to "unavailable" — modules only need to know
  "translation is not working right now" vs. "quota is exhausted", per the
  ticket's acceptance criteria. Both live next to the port in `platform.i18n`,
  following `errors.md`'s "public exception next to the port" convention.
- `TranslationRequest` carries an already-resolved `glossaryReference` (a
  plain `String`); glossary creation/reuse per business is an adapter-level
  capability (`DeepLGlossaries`), not part of the kernel contract — catalog
  and menu (later tickets) decide when to create one.
- No Spring context in the adapter tests: `DeepLMachineTranslator` is
  instantiated directly against a WireMock server, mirroring
  `ShortLivedSecretStoreIntegrationTests`' direct-instantiation style but
  without Testcontainers since there is no broker/DB dependency yet.

## Open questions

- None blocking; catalog/menu tickets will decide the translation table shape
  per `i18n.md`'s existing "Tenant content" section (unchanged by this ticket).
