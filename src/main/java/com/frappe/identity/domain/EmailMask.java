package com.frappe.identity.domain;

/**
 * The one way identity prints an email address in logs, exceptions and {@code toString}: its first code point and its
 * domain ({@code a***@example.com}), so a line never carries the full address.
 */
public final class EmailMask {

    private static final String HIDDEN = "***";

    private EmailMask() {}

    /**
     * Masks an address.
     *
     * @param address the address, not {@code null}
     * @return the first code point, {@code ***} and the domain; only {@code ***} without a usable {@code @}
     */
    public static String of(String address) {
        var at = address.lastIndexOf('@');
        if (at <= 0) {
            return HIDDEN;
        }
        var firstCodePoint = address.offsetByCodePoints(0, 1);
        if (firstCodePoint > at) {
            return HIDDEN + address.substring(at);
        }
        return address.substring(0, firstCodePoint) + HIDDEN + address.substring(at);
    }
}
