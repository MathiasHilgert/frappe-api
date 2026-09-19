# FAPI-15: Send transactional email through a Mailer port

## Objective

Modules send transactional email through the platform kernel port `Mailer#send(MailMessage)`. Each message is rendered
in exactly one language (JTE templates, text from the ICU catalogs, MJML layout compiled at build time) and delivered by
Resend outside `local`, by SMTP to Mailpit in `local`, and by a recording adapter in tests. Sending is triggered by
domain event listeners after the commit, so the outbox provides retries.

## Why

Identity must send verification, reset and email-change codes (FAPI tickets 08, 13, 14 are blocked by this one).

## Scope

- Kernel `com.frappe.platform.mail`: `Mailer`, `MailMessage`, `MailDeliveryException` (plain Java, ArchUnit-checked).
- `platform.infrastructure.mail`: JTE renderer with whole-message locale fallback (`mail.locale.fallback`), Resend and
  SMTP transports, `mail.send` observation, fail-fast settings (`RESEND_API_KEY`, `FRAPPE_MAIL_FROM` outside `local`).
- Build: JTE 3.2.4 (gg.jte.gradle, generate mode), MJML layout via node-gradle 7.1.0 (generated HTML not committed).
- compose: `axllent/mailpit:v1.31` (SMTP 1025, UI 8025, overridable ports); `application-local.properties`.
- Docs: README (variables, Mailpit), `writing-code/references/email.md`, errors/logging/observability references.

## Out of scope

Modules' own templates; bounces, webhooks, attachments, marketing mail.

## Constraints

- Libraries only in infrastructure adapters; modules depend on `Mailer`/`MailMessage` only.
- Never log bodies, API keys or full recipient addresses; never call real Resend in tests.
- Env var name for the Resend key is `RESEND_API_KEY` (coordinator: Bitwarden name), not `FRAPPE_RESEND_API_KEY`.

## TDD

- Mode: strict (project `testing-code` skill, CLAUDE.md golden rule 4).
- Runner: `FRAPPE_TEST_DB=frappe_fapi_15 ./gradlew test`; gate `./gradlew spotlessApply check --rerun-tasks`.

## Deviations from the Plane ticket

- Env var `RESEND_API_KEY` instead of the ticket's `FRAPPE_RESEND_API_KEY`: it is the name the key has in Bitwarden
  (`frappe-dev`, `frappe-production`), decided by the orchestrator; the Plane ticket still says `FRAPPE_RESEND_API_KEY`
  and needs updating after merge.
- Permanent provider rejections are not retried (not in the ticket): logged once at ERROR, counted, publication
  completes (orchestrator decision 2, see T9).
- Every mail is multipart (HTML + text), orchestrator decision 3.
- Size ~2,400+ lines in one PR: exception recorded by the orchestrator.
- `PlainTextAlternative` is our own ~60-line visitor over jsoup (accepted deviation from "nothing written by hand
  beyond the adapters"): jsoup parses and decodes but has no formatted text output (its `text()` drops line breaks).
- The renderer's whole-message fallback on a missing key is a backstop: the startup catalog check already requires the
  same keys in all three languages, so in production it triggers only for unsupported locales.

## Tasks

- [x] T0. Verify versions/APIs: resend-java 4.26.0 (latest; base URL fixed `https://api.resend.com`, `Idempotency-Key`
      via `RequestOptions`), JTE 3.2.4 (latest; plugin `generate()` mode), node-gradle 7.1.0 (latest), mjml 5.4.1
      (latest; `mj-raw position="file-start"`), Node 24.21.0 LTS, `axllent/mailpit:v1.31` (tag exists, v1.31.2 patch).
- [x] T1. Kernel port `Mailer`, `MailMessage` (validation), `MailDeliveryException`; kernel dependency test.
- [x] T2. Build: JTE generate for main and test templates, MJML layout compiled to a JTE template.
- [x] T3. Renderer: one locale per message, whole-message fallback counted by `mail.locale.fallback`.
- [x] T4. Transports: Resend (idempotency key), SMTP (Mailpit), `mail.send` observation, privacy of logs/spans.
- [x] T5. Configuration: provider selection, fail-fast settings, compose Mailpit, local properties.
- [x] T6. Outbox example: listener fails on 5xx, recovery pass sends after the stub recovers.
- [x] T7. Docs and skills: README, `email.md`, errors/logging/observability references.
- [x] T9. Review decisions: `ObservedMailer` no longer logs transient failures (log or rethrow); permanent rejections
      (Resend 4xx except 401/403/408/409/429, SMTP 5xx, malformed address) are logged once and not retried; multipart
      HTML + plain text (jsoup-derived).
- [x] T10. Deep-review minors: ISO-8859-1-safe `FRAPPE_MAIL_FROM` (`Frapp\u00e9`) with a From-name assertion;
      transient-only Javadoc on `Mailer`/`MailDeliveryException`/`MailTransport`; sender validated at startup; Resend
      configuration errors transient; SMTP permanent only for a 5xx refused recipient; CI npm cache,
      `npm ci --ignore-scripts`, node version input and cacheable `compileMailLayouts`; `MailLibrariesTest`;
      `RecordingMailer` fixture; `frappe.mail.provider` log field; comment and import cleanups.
- [x] T8. Gate `./gradlew spotlessApply check --rerun-tasks` green; commit.

## Acceptance → tests

| Acceptance | Test |
| --- | --- |
| Mail sent with compose appears in Mailpit | `MailpitIntegrationTests` (Testcontainers Mailpit, same image) |
| Complete es template → subject and body entirely Spanish | `MailRendererTest` |
| Missing key → whole mail in fallback, counter with tags | `MailRendererTest` |
| No API key outside local → startup fails naming the variable | `MailConfigurationTests` |
| Resend 5xx (stubbed) → publication incomplete, recovery sends | `MailOutboxIntegrationTests` |
| No body, API key or full address in logs/spans | `ResendMailTransportTest`, `ObservedMailerTest` |
| Compose Mailpit, local profile | `LocalComposeStackTest`, `LocalProfileTest` |

## Progress / evidence

Recorded per task below as work proceeds.

- T1 RED: `MailMessageTest`, `MailKernelDependenciesTest` fail to compile (`cannot find symbol MailMessage`). GREEN: 17 + 1
  tests pass.

- T2 RED: `MailLayoutTest` build fails (`generateJte`: source directory `src/main/jte` does not exist, no layout).
  GREEN: 1 test passes after the MJML → JTE pipeline (`compileMailLayouts`, `assembleJteSources`, `generateJte`).
- T3 RED: `MailRendererTest` fails to compile (`cannot find symbol MailRenderer`, `MailTexts`). GREEN: 8 tests pass.

- T4 RED: `ResendMailTransportTest`, `ObservedMailerTest` fail to compile (`cannot find symbol ObservedMailer`,
  `ResendMailTransport`, `SmtpMailTransport`, `MailTransport`). GREEN: 5 + 4 tests pass (incl. no recipient, code or
  API key in logs/spans for the Resend and SMTP adapters; one ERROR log with ECS fields on failure).

- T5 RED: `LocalComposeStackTest.runsMailpitForLocalMail` (no mailpit service), `LocalProfileTest` (no `spring.mail.*`),
  `MailConfigurationTests` (startup got to JPA instead of failing on mail settings), `MailpitIntegrationTests` (no
  `Mailer` bean). GREEN: all pass; `OpenApiOutsideLocalProfileTests` given the two mail variables.

- T6: `MailOutboxIntegrationTests` passed on first run (behaviour already built in T4/T5). Validity check (mutation):
  making `ObservedMailer` swallow `MailDeliveryException` fails it (`ConditionTimeoutException`, publication completed
  without delivery); restored. Runs on its own non-reused Postgres so other cached contexts' recovery jobs cannot
  dead-letter the test listener's failed publication (`UNKNOWN_LISTENER`).
- T7: README (Mail section, variables, Mailpit ports), `writing-code/references/email.md` (new), errors (provider
  failures never reach clients: translate, log once at ERROR, count), logging, observability, i18n, testing
  integration-tests.

- T8: first gate run failed 4 health tests (Boot's mail health indicator DOWN without Mailpit); fixed with
  `management.health.mail.enabled=false` (mail outage only delays mail). Gate
  `FRAPPE_TEST_DB=frappe_fapi_15 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 433 tests, 0 failures.

- Rebased onto origin/main 3d59f21 (PR #19 secrets, PR #14 use cases landed meanwhile); one conflict in
  `writing-code/references/observability.md` resolved by keeping both lists. `docs/secrets.md` now on main: row for
  `RESEND_API_KEY`, `FRAPPE_MAIL_FROM` under configuration. Gate after rebase: BUILD SUCCESSFUL, 470 tests, 0 failures.
- Size: about 2,400 changed lines without `package-lock.json`, well above the ~400 heuristic (tests are about half);
  split candidates if review asks: kernel+renderer+build / transports+config / outbox test+docs.

- T9 RED: compile errors (`MailRejectedException`, `PlainTextAlternative`, `RenderedMail.text` missing) for
  `ResendMailTransportTest` (permanent/transient statuses, text part), `SmtpMailTransportTest` (550 vs 451, closed port,
  multipart/alternative), `PlainTextAlternativeTest`, `ObservedMailerTest` (no log on transient, one ERROR on rejection),
  `MailRendererTest` (text), `MailpitIntegrationTests` (Text part; Mailpit refuses non-example.com with 550),
  `MailOutboxIntegrationTests` (ERROR count == failed attempts; 422 completes after one attempt, one ERROR).
  GREEN: all mail tests pass. Gate after T9: BUILD SUCCESSFUL, 491 tests, 0 failures (first run failed on one
  assertion counting unrelated background NATS ERRORs of other cached contexts; now counted by exception type).
  Logging verified end to end: without `ObservedMailer`'s log, each failed attempt yields exactly one ERROR, from
  Spring's async uncaught-exception handler around the `@ApplicationModuleListener` (plus Modulith's INFO).

- T10 (gate: BUILD SUCCESSFUL, 501 tests, 0 failures) RED: `MailpitIntegrationTests` (From name "FrappÃ©"), `MailConfigurationTests.startupFailsOnASenderThatIsNotAnAddress`,
  `ResendMailTransportTest.configurationErrorsAreTransientWhateverTheirStatus` (4 cases),
  `SmtpMailTransportTest.aPermanentReplyAboutTheSenderOrTheSessionIsTransient` (530), `ObservedMailerTest` (log field
  `frappe.mail.provider`), `RecordingMailerTest` (compile: no `RecordingMailer`). GREEN after the fixes.
  `MailLibrariesTest` is a guard and passed on first run.

## Engram mirror

Pending: no Engram tool available to this worker; the orchestrator mirrors `odd/fapi-15-mailer/tasks`.
