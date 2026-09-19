package com.frappe.platform.mail;

import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.regex.Pattern;

/**
 * One transactional email to one recipient, rendered in one language.
 *
 * @param templateId the template, {@code <module>/<name>} in lowercase kebab-case ({@code identity/email-proof}); its
 *     subject is the catalog key {@code <module>.mail.<name>.subject}
 * @param recipient the recipient's address; never logged or traced in full
 * @param locale the language to render in, usually the recipient's preferred one
 * @param fallbackLocale the language used for the whole message when the template's text is incomplete in
 *     {@code locale}: the business or tenant language, else English
 * @param model the values the template shows, by parameter name (immutable copy; no {@code null} values)
 * @param idempotencyKey the provider's deduplication key for retries of identical content, at most 256 characters
 */
public record MailMessage(
        String templateId,
        String recipient,
        Locale locale,
        Locale fallbackLocale,
        Map<String, Object> model,
        Optional<String> idempotencyKey) {

    private static final Pattern TEMPLATE_ID = Pattern.compile("[a-z][a-z0-9]*/[a-z0-9]+(-[a-z0-9]+)*");

    private static final Pattern ADDRESS = Pattern.compile("[^@\\s]+@[^@\\s]+");

    /** Resend's limit for an idempotency key. */
    private static final int MAX_IDEMPOTENCY_KEY_LENGTH = 256;

    /**
     * Validates the message.
     *
     * @throws IllegalArgumentException if the template id is not {@code <module>/<name>} in kebab-case, the recipient is
     *     not an address or the idempotency key is blank or longer than 256 characters
     * @throws NullPointerException if a part is missing
     */
    public MailMessage {
        Objects.requireNonNull(templateId, "templateId");
        Objects.requireNonNull(recipient, "recipient");
        Objects.requireNonNull(locale, "locale");
        Objects.requireNonNull(fallbackLocale, "fallbackLocale");
        Objects.requireNonNull(idempotencyKey, "idempotencyKey");
        model = Map.copyOf(model);
        if (!TEMPLATE_ID.matcher(templateId).matches()) {
            throw new IllegalArgumentException(
                    "Template id '%s' must be <module>/<name> in lowercase kebab-case".formatted(templateId));
        }
        if (!ADDRESS.matcher(recipient).matches()) {
            throw new IllegalArgumentException("Recipient is not an email address");
        }
        idempotencyKey.ifPresent(MailMessage::requireValidIdempotencyKey);
    }

    /**
     * Creates a message without an idempotency key.
     *
     * @param templateId the template, {@code <module>/<name>}
     * @param recipient the recipient's address
     * @param locale the language to render in
     * @param fallbackLocale the language used when the template's text is incomplete in {@code locale}
     * @param model the values the template shows, by parameter name
     * @return the message
     */
    public static MailMessage of(
            String templateId, String recipient, Locale locale, Locale fallbackLocale, Map<String, ?> model) {
        return new MailMessage(templateId, recipient, locale, fallbackLocale, Map.copyOf(model), Optional.empty());
    }

    /**
     * Returns this message with a provider idempotency key, so a retry within the provider's window (24 hours for
     * Resend) is not delivered twice.
     *
     * @param key the key, for example {@code email-proof/<eventId>}; at most 256 characters
     * @return the message with the key
     */
    public MailMessage withIdempotencyKey(String key) {
        return new MailMessage(templateId, recipient, locale, fallbackLocale, model, Optional.of(key));
    }

    /**
     * Returns the recipient with its local part masked ({@code a***@example.com}), the only form of the address that
     * may appear in logs and spans.
     *
     * @return the masked recipient
     */
    public String maskedRecipient() {
        var at = recipient.lastIndexOf('@');
        return recipient.charAt(0) + "***" + recipient.substring(at);
    }

    /**
     * Describes the message without its model or the full recipient, which may carry one-time codes and personal data.
     *
     * @return the template, the masked recipient and the locales
     */
    @Override
    public String toString() {
        return "MailMessage[templateId=%s, recipient=%s, locale=%s, fallbackLocale=%s, idempotencyKey=%s]"
                .formatted(
                        templateId,
                        maskedRecipient(),
                        locale.toLanguageTag(),
                        fallbackLocale.toLanguageTag(),
                        idempotencyKey.isPresent() ? "present" : "absent");
    }

    private static void requireValidIdempotencyKey(String key) {
        if (key.isBlank() || key.length() > MAX_IDEMPOTENCY_KEY_LENGTH) {
            throw new IllegalArgumentException("Idempotency key must be non-blank and at most %d characters"
                    .formatted(MAX_IDEMPOTENCY_KEY_LENGTH));
        }
    }
}
