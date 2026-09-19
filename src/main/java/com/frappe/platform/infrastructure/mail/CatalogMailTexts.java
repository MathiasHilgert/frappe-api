package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailTexts;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.SequencedSet;
import org.springframework.context.MessageSource;
import org.springframework.context.NoSuchMessageException;

/**
 * {@link MailTexts} over the application message source in one locale, for one rendering. A missing key renders as an
 * empty string and is remembered, so the renderer can discard the attempt and render the whole mail in another language.
 * Not thread-safe: one instance per rendering.
 */
final class CatalogMailTexts implements MailTexts {

    private final MessageSource messages;

    private final Locale locale;

    private final SequencedSet<String> missingKeys = new LinkedHashSet<>();

    /**
     * Creates the texts of one rendering.
     *
     * @param messages the application message source
     * @param locale the supported locale to resolve in
     */
    CatalogMailTexts(MessageSource messages, Locale locale) {
        this.messages = messages;
        this.locale = locale;
    }

    @Override
    public String get(String key, Object... args) {
        try {
            return messages.getMessage(key, args, locale);
        } catch (NoSuchMessageException missing) {
            // Expected when a catalog lacks the key in this language: the renderer falls back for the whole mail.
            missingKeys.add(key);
            return "";
        }
    }

    /**
     * Returns the keys asked for that this locale lacks.
     *
     * @return the missing keys, in the order they were asked for
     */
    SequencedSet<String> missingKeys() {
        return Collections.unmodifiableSequencedSet(missingKeys);
    }
}
