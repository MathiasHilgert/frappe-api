package com.frappe.identity;

/** Prints a recipient for logs and exceptions: first character and domain only ({@code a***@example.com}). */
final class MaskedRecipient {

    private MaskedRecipient() {}

    /**
     * Masks a recipient.
     *
     * @param recipient the address
     * @return the first character and the domain, or {@code ***} without a usable {@code @}
     */
    static String of(String recipient) {
        var at = recipient.lastIndexOf('@');
        if (at <= 0) {
            return "***";
        }
        return recipient.charAt(0) + "***" + recipient.substring(at);
    }
}
