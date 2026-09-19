package com.frappe.platform;

/**
 * The store behind {@link ShortLivedSecretStore} (Valkey) cannot be reached or did not answer in time. Callers fail the request as temporarily unavailable; they never skip the check.
 */
public final class SecretStoreUnavailableException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message what could not be done, without secrets or personal data
     * @param cause the client failure
     */
    public SecretStoreUnavailableException(String message, Throwable cause) {
        super(message, cause);
    }
}
