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

## Tasks

- [x] T0. Verify versions/APIs: resend-java 4.26.0 (latest; base URL fixed `https://api.resend.com`, `Idempotency-Key`
      via `RequestOptions`), JTE 3.2.4 (latest; plugin `generate()` mode), node-gradle 7.1.0 (latest), mjml 5.4.1
      (latest; `mj-raw position="file-start"`), Node 24.21.0 LTS, `axllent/mailpit:v1.31` (tag exists, v1.31.2 patch).
- [x] T1. Kernel port `Mailer`, `MailMessage` (validation), `MailDeliveryException`; kernel dependency test.
- [x] T2. Build: JTE generate for main and test templates, MJML layout compiled to a JTE template.
- [x] T3. Renderer: one locale per message, whole-message fallback counted by `mail.locale.fallback`.
- [x] T4. Transports: Resend (idempotency key), SMTP (Mailpit), `mail.send` observation, privacy of logs/spans.
- [ ] T5. Configuration: provider selection, fail-fast settings, compose Mailpit, local properties.
- [ ] T6. Outbox example: listener fails on 5xx, recovery pass sends after the stub recovers.
- [ ] T7. Docs and skills: README, `email.md`, errors/logging/observability references.
- [ ] T8. Gate `./gradlew spotlessApply check --rerun-tasks` green; commit.

## Acceptance → tests

| Acceptance | Test |
| --- | --- |
| Mail sent with compose appears in Mailpit | `SmtpMailpitIntegrationTests` (Testcontainers Mailpit, same image) |
| Complete es template → subject and body entirely Spanish | `MailRendererTest` |
| Missing key → whole mail in fallback, counter with tags | `MailRendererTest` |
| No API key outside local → startup fails naming the variable | `MailConfigurationTests` |
| Resend 5xx (stubbed) → publication incomplete, recovery sends | `MailOutboxIntegrationTests` |
| No body, API key or full address in logs/spans | `ResendMailTransportTest`, `ObservedMailerTest` |

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

## Engram mirror

Pending: no Engram tool available to this worker; the orchestrator mirrors `odd/fapi-15-mailer/tasks`.
