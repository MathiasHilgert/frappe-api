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

`MailOutboxIntegrationTests` proves the loop: Resend answers 503, the publication stays incomplete, a recovery pass sends it once the provider is back.

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
- Outside `local`, `RESEND_API_KEY` and `FRAPPE_MAIL_FROM` are required; `RequiredMailSettings` fails startup naming the missing ones.
- Every send is one `mail.send` observation (span and timer: `mail.provider`, `mail.template`, `mail.locale`, `error`); its `error` tag counts failed deliveries. Add no telemetry around `Mailer`.
- A provider failure becomes `MailDeliveryException` (our kernel type), logged once at ERROR by `ObservedMailer` (`frappe.mail.template`, `mail.provider`, `frappe.mail.locale`) and rethrown so the listener fails; callers do not log it again (`errors.md`).

## Privacy

- Never log or trace the recipient, the subject, the body or the API key. `MailMessage#toString` masks the recipient (`a***@example.com`) and omits the model; `maskedRecipient()` is the only form allowed in logs.
- Provider errors are not chained as causes when their text can echo the request (Resend error bodies, SMTP replies): the exception keeps the status and error name only.
- `ObservedMailerTest` checks both adapters: no recipient, code or key in any log line, key value or span.

## Tests

- Unit: `MailRenderer` with a `StaticMessageSource` (incomplete languages are impossible in the checked catalogs); transports with a stubbed `ResendEmails` or an unreachable SMTP port.
- Integration: `TestMailpitConfiguration` (Mailpit container, `spring.mail.*`), and read the mail through Mailpit's API (`/api/v1/search?query=to:<address>`).
- Never call the real Resend API; stub `ResendEmails` (`@Primary` bean), the SDK's base URL is fixed.
