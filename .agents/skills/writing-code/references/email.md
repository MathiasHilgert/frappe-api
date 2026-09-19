# Email

Transactional mail goes through the kernel port `com.frappe.platform.mail.Mailer` (`send(MailMessage)`). Modules never import JTE, Resend, Spring Mail or Jakarta Mail; `MailKernelDependenciesTest` keeps the kernel plain Java. Out of scope so far: bounces, webhooks, attachments, marketing mail.

## Send from a listener, never from a command

Mail cannot be rolled back, so it is sent only after the commit, from an event listener. A failure fails the listener and the outbox retries it (`domain-events.md`, recovery). Delivery is therefore at least once: pass an idempotency key whenever a retry would send identical content (Resend drops a repeated key for 24 hours).

```java
@Component
class EmailProofMails {
    private final Mailer mailer;                                   // com.frappe.platform.mail.Mailer

    EmailProofMails(Mailer mailer) {
        this.mailer = mailer;
    }

    @ApplicationModuleListener                                     // after commit, own transaction, retried by the outbox
    void on(EmailProofRequested event) {
        mailer.send(MailMessage.of(
                        "identity/email-proof",                     // <module>/<name>
                        event.email(),
                        event.locale(),                             // the recipient's language
                        event.tenantLocale(),                       // fallback: business or tenant language, else en
                        Map.of("code", event.code()))
                .withIdempotencyKey("email-proof/" + event.eventId()));
    }
}
```

`MailOutboxIntegrationTests` proves the loop: Resend answers 503, the publication stays incomplete, a recovery pass sends it once the provider is back, and every failed attempt is logged exactly once. A permanent rejection (422) completes the publication after one attempt and one ERROR.

## Templates

- One JTE template per mail: `src/main/jte/<module>/<name>.jte`, the body only (the platform wraps it in the layout). Parameters: `@param com.frappe.platform.mail.MailTexts texts` (reserved name) plus the model's keys.
- Every sentence comes from the module's catalogs through `texts.get(key, args...)`; never literals. The subject is the key `<module>.mail.<name>.subject`. Keys follow `i18n.md` (`identity.mail.email-proof.code`), in all three catalogs.
- `${...}` is HTML-escaped (JTE `ContentType.Html`); model values are never trusted HTML.
- Templates are generated to Java at build time (`generateJte`, gg.jte.gradle 3.2.4) and run precompiled (`jte-runtime`); a template error is a compile error.
- The layout is MJML (`src/main/mjml/mail/layout.mjml`), compiled to `build/generated/mjml/mail/layout.jte` by `compileMailLayouts` (mjml 5, Node downloaded by node-gradle); the HTML is never committed. JTE directives go in `<mj-raw position="file-start">`; no web fonts (MJML's CSS `@import` would read as a JTE directive).
- Test-only templates live in `src/test/jte` (`generateTestJte`), with test catalogs in `src/test/resources/i18n/<module>`.

```html
@import com.frappe.platform.mail.MailTexts
@param MailTexts texts
@param String code
<p>${texts.get("identity.mail.email-proof.code", code)}</p>
```

## One language per mail

- The mail renders in `locale` mapped to its supported language (`es-AR` → `es`). If that language is unsupported or lacks any key the template or its subject needs, the whole mail is rendered again in `fallbackLocale`, then English; never mixed.
- Each fallback counts `mail.locale.fallback` (tags `mail.template`, `locale.requested` — the language or `unsupported` —, `locale.used`). A key missing in every language is a bug (`IllegalStateException` naming the keys).

## Delivery

- `frappe.mail.provider`: `resend` (default) or `smtp`. The `local` profile uses SMTP to compose's Mailpit (UI <http://localhost:8025>).
- Outside `local`, `RESEND_API_KEY` and `FRAPPE_MAIL_FROM` are required; `RequiredMailSettings` fails startup naming the missing ones, and when `FRAPPE_MAIL_FROM` is not an RFC 822 address (`InternetAddress`, strict).
- `.properties` files are read as ISO-8859-1: write non-ASCII in them as `\u00e9` (`FRAPPE_MAIL_FROM=Frapp\u00e9 <...>` in `application-local.properties`); environment variables are fine as UTF-8.
- What is retried: a failure a retry or an operator can fix. Resend: 5xx, 401/403/408/409/429, configuration errors under any status (`invalid_from_address`, `invalid_api_key`, `missing_api_key`, `restricted_api_key`, `invalid_access`), network. SMTP: 4xx, every 5xx except a refused recipient (530 authentication required, 550 sender not permitted are settings), connection. What is not: Resend's other 4xx (400/422 validation, invalid recipient) and an SMTP 5xx reply to RCPT TO (`SMTPAddressFailedException`).
- Every mail is `multipart/alternative`: the HTML and a plain-text part derived from the rendered body (`PlainTextAlternative`, jsoup: blank line between blocks, a line per `<br>` and list item, a link's address after its text). No text template per mail.
- Every send is one `mail.send` observation (span and timer: `mail.provider`, `mail.template`, `mail.locale`, `mail.outcome` = `sent` | `rejected` | `failed`, `error`). Add no telemetry around `Mailer`.
- Transient failures (outages, limits, fixable settings) throw `MailDeliveryException` (our kernel type) out of `send`, unlogged: the listener fails, its async boundary logs once, the outbox retries. Permanent rejections (invalid recipient, a request the provider never accepts) are logged once at ERROR by `ObservedMailer` and `send` returns normally, so they are not retried (`errors.md`).

## Privacy

- Never log or trace the recipient, the subject, the body or the API key. `MailMessage#toString` masks the recipient (`a***@example.com`) and omits the model; `maskedRecipient()` is the only form allowed in logs.
- Provider errors are not chained as causes when their text can echo the request (Resend error bodies, SMTP replies): the exception keeps the status and error name only.
- `ObservedMailerTest` checks both adapters: no recipient, code or key in any log line, key value or span.

## Tests

- Modules: register `com.frappe.platform.mail.RecordingMailer` (test sources) as a `@Primary` bean, publish the event, and assert on `sent()` / `sentTo(address)`; `failNextSend()` simulates an outage. Mail listeners are asynchronous: wait with Awaitility.
- Unit: `MailRenderer` with a `StaticMessageSource` (incomplete languages are impossible in the checked catalogs, so the whole-message fallback is a backstop behind the startup catalog check); transports with a stubbed `ResendEmails` or an unreachable SMTP port.
- `MailLibrariesTest` (ArchUnit): `com.resend..`, `gg.jte..`, `org.jsoup..`, `jakarta.mail..`, `org.eclipse.angus..` only in `platform.infrastructure.mail` and JTE's generated templates.
- Integration: `TestMailpitConfiguration` (Mailpit container, `spring.mail.*`), and read the mail through Mailpit's API (`/api/v1/search?query=to:<address>`).
- Never call the real Resend API; stub `ResendEmails` (`@Primary` bean), the SDK's base URL is fixed.
