package com.frappe.platform.infrastructure.i18n;

/** A message catalog could not be found or read (an I/O failure or a malformed properties file). */
final class UnreadableMessageCatalogException extends RuntimeException {

    /**
     * Creates the exception.
     *
     * @param message what could not be read, and where
     * @param cause the underlying failure
     */
    UnreadableMessageCatalogException(String message, Throwable cause) {
        super(message, cause);
    }
}
