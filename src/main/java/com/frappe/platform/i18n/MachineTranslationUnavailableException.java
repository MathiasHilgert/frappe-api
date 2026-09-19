package com.frappe.platform.i18n;

/**
 * Machine translation cannot be performed right now: no provider key is configured, the provider could not be
 * reached, did not answer within the bounded timeout, or refused the key. Callers fail the request as temporarily
 * unavailable; they never retry silently.
 *
 * <p>The message never names the provider or carries its response, so it stays safe wherever an exception message
 * might be rendered (the web layer never renders it anyway: unexpected exceptions become a generic localized 500);
 * provider detail belongs only in the adapter's own log line and the {@linkplain #getCause() cause}, never here.
 */
public final class MachineTranslationUnavailableException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message what could not be done, naming neither the provider nor its response
     */
    public MachineTranslationUnavailableException(String message) {
        super(message);
    }

    /**
     * Creates the exception with the provider failure as cause.
     *
     * @param message what could not be done, naming neither the provider nor its response
     * @param cause the provider failure, for the log line that reports it; never rendered to a client
     */
    public MachineTranslationUnavailableException(String message, Throwable cause) {
        super(message, cause);
    }
}
