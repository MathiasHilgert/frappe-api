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
- [x] T1 kernel ports: `MachineTranslator`, `MachineTranslationGlossaries`,
      `TranslationRequest`, `Translation`, `TranslationFormality`,
      `MachineTranslationUnavailableException`,
      `MachineTranslationQuotaExceededException` in `com.frappe.platform.i18n`
      (plain Java only; `I18nKernelDependenciesTest` covers it, no new ArchUnit
      test needed).
- [x] T2 DeepL adapter `DeepLMachineTranslator` in
      `com.frappe.platform.infrastructure.i18n`, contract-tested against a
      WireMock stub server, built through `DeepLTranslationConfiguration`.
- [x] T3 missing-key / unavailable behavior + startup does not fail (both
      `MachineTranslator` and `MachineTranslationGlossaries`).
- [x] T4 telemetry: `translation.request` observation, `translation.characters`
      counter, tags bare target language + outcome (low cardinality; `error`
      default for anything not explicitly mapped, via `Observation#observe`).
- [x] T5 fakes `FakeMachineTranslator`/`FakeMachineTranslationGlossaries` in
      `com.frappe.platform.i18n` test sources, for other modules' tests.
- [x] T6 glossary support: `MachineTranslationGlossaries` kernel port,
      `DeepLGlossaries` adapter creates-or-reuses one glossary per business
      (contract-tested, including "exists for another pair" and the
      cross-instance dedup race).
- [x] T7 wiring: `DeepLTranslationProperties` (`frappe.translation.deepl.*`,
      including `default-variants.*`), `DeepLTranslationConfiguration`,
      `application.properties` entry for `FRAPPE_DEEPL_API_KEY` (optional,
      empty default).
- [x] T8 docs: update `writing-code/references/i18n.md` (machine translation)
      and `errors.md` (sanitizing a third-party boundary, the no-fail-fast
      exception).
- [x] T9 `./gradlew spotlessApply check` green.

## Verification evidence

`FRAPPE_TEST_DB=frappe_fapi_34 ./gradlew spotlessApply check --rerun-tasks` → BUILD SUCCESSFUL (both before and
after the review round). See report to caller for RED/GREEN test names.

## Review round (deep review, changes requested)

Addressed in commit `d972b11` (after `b1391ac`):

- **Logging moved out of the adapter.** `DeepLMachineTranslator`/`DeepLGlossaries` no longer log; they only tag the
  `translation.request` observation's `outcome` (`success`, `unavailable`, `invalid_key`, `quota_exceeded`, or
  `error` for anything not explicitly mapped, via `Observation#observe`'s own error handling — no
  `catch (RuntimeException)` anywhere) and rethrow a sanitized kernel exception with the provider failure as
  `cause`. `errors.md` now documents this "sanitizing a third-party adapter boundary" pattern and the deliberate
  exception to "fail fast at startup" for an optional external capability (`FRAPPE_DEEPL_API_KEY`).
- **`MachineTranslationGlossaries` kernel port added** (`ensure(businessId, source, target, entries)`), backed by
  `DeepLGlossaries`: one glossary per business (`frappe-business-<uuid>`), one dictionary per pair inside it, added
  or replaced via DeepL's `replaceMultilingualGlossaryDictionary` (create-or-replace, so "exists for another pair"
  and "entries changed" are the same code path), a per-instance `ConcurrentHashMap` cache keyed by business+pair so
  a repeated `ensure()` never lists the account again, and oldest-wins deduplication (list, sort by
  `creationTime`, delete every same-name glossary but the oldest) run both before creating and, to resolve a
  cross-instance race, again right after.
- **Target-language mapping fixed**: explicit DeepL-supported variants (`en-GB`, `en-US`, `pt-PT`, `pt-BR`,
  `es-419`) are sent as-is; any other region is reduced to bare; a bare language still needing a region (`en`,
  `pt`) gets it from `frappe.translation.deepl.default-variants.*` (a configurable `Map<String,String>` on
  `DeepLTranslationProperties`, not a hardcoded switch). Source language is always sent bare. Found by running the
  new parameterized test (`mapsTheTargetLanguageToTheVariantDeepLAccepts`) against the real SDK's client-side
  `checkValidLanguages` validation.
- **`TranslationRequest` rejects a `glossaryReference` without a `sourceLanguage`** (the SDK itself throws
  `IllegalArgumentException` for the same combination — belt and suspenders at our own boundary).
- **Detected source language mapped to a supported locale** (`mapToSupportedLocale`) instead of constructing a
  `Locale` directly from DeepL's code (which could be a region-qualified or unsupported language).
- **`DeepLTranslationProperties#toString()` masks the key.**
- **`FakeMachineTranslator` moved** to `com.frappe.platform.i18n` (test sources), alongside a new
  `FakeMachineTranslationGlossaries`; both are documented as existing for deterministic test output, not as the
  "no key" story (the DeepL adapter itself already handles that by reporting unavailable).
- **Tests rebuilt** to go through `DeepLTranslationConfiguration`'s bean methods (same `maxRetries(0)`, timeout,
  `serverUrl` wiring as production) instead of constructing a `DeepLClient` ad hoc, with realistic WireMock stubs
  (request body assertions for `source_lang`/`target_lang`/`glossary_id`, the `Authorization: DeepL-Auth-Key`
  header, JSON error bodies matching DeepL's `{"message": "..."}` shape), one-request-per-failure and
  elapsed-time-bound assertions, and `TestObservationRegistry`/`SimpleMeterRegistry` assertions of the outcome
  tags and the characters counter (including "no counter recorded on a failed call").
- `git fetch && git merge origin/main` after the round: already up to date (no new commits upstream since the
  earlier merge that pulled in `docs/secrets.md` and FAPI-11).

## Decisions

- Kernel exceptions: only two dedicated types (`MachineTranslationUnavailableException`,
  `MachineTranslationQuotaExceededException`); auth failure, timeout and connection failure all map to
  "unavailable" except an invalid key, which is also "unavailable" to the caller (the same exception type) but
  tagged `invalid_key` on the observation for operators — modules only need to know "translation is not working
  right now" vs. "quota is exhausted", per the ticket's acceptance criteria. Both live next to the port in
  `platform.i18n`, following `errors.md`'s "public exception next to the port" convention.
- `TranslationRequest` carries an already-resolved `glossaryReference` (a plain `String`); glossary creation/reuse
  per business is `MachineTranslationGlossaries`, a separate kernel port from `MachineTranslator` — catalog and
  menu (later tickets) decide when to call `ensure()` and pass the resulting reference on later translate calls.
- No Spring context in the adapter tests: both adapters are built through `DeepLTranslationConfiguration`'s bean
  methods directly (not a full `@SpringBootTest`) against a WireMock server, mirroring
  `ShortLivedSecretStoreIntegrationTests`' direct-instantiation style but without Testcontainers since there is no
  broker/DB dependency yet.
- DeepL key is Free plan (`:fx` suffix); the SDK's `Translator` constructor already selects `api-free.deepl.com`
  from that suffix when `serverUrl` is not overridden, so production wiring needs no extra branching — only tests
  override `serverUrl` to point at WireMock.
- `docs/secrets.md` landed on `main` (PR #19) mid-ticket; branch merged `origin/main`, `FRAPPE_DEEPL_API_KEY` added
  to its "Secrets the application reads" table.

## Open questions

- None blocking; catalog/menu tickets will decide the translation table shape per `i18n.md`'s existing "Tenant
  content" section (unchanged by this ticket), and will be the first real caller of `MachineTranslationGlossaries`.
