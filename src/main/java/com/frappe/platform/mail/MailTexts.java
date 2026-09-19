package com.frappe.platform.mail;

/**
 * The text of one mail in one language, handed to every mail template as its {@code texts} parameter. Templates take
 * every user-facing sentence from it, never from literals:
 *
 * <pre>{@code
 * @import com.frappe.platform.mail.MailTexts
 * @param MailTexts texts
 * @param String code
 * <p>${texts.get("identity.mail.email-proof.code", code)}</p>
 * }</pre>
 *
 * <p>When a key is missing in the mail's language, the platform renders the whole mail again in the fallback language;
 * templates never handle that themselves.
 */
public interface MailTexts {

    /**
     * Resolves a catalog message in the mail's language.
     *
     * @param key the message key, for example {@code identity.mail.email-proof.code}
     * @param args the values of the numbered placeholders, in order
     * @return the formatted message
     */
    String get(String key, Object... args);
}
