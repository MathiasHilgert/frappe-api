package com.frappe.platform.i18n;

/**
 * The provider's translation quota (billed characters) is exhausted for the current period.
 *
 * <p>The message never names the provider; see {@link MachineTranslationUnavailableException} for why.
 */
public final class MachineTranslationQuotaExceededException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message what could not be done, naming neither the provider nor its response
     * @param cause the provider failure, for the log line that reports it; never rendered to a client
     */
    public MachineTranslationQuotaExceededException(String message, Throwable cause) {
        super(message, cause);
    }
}
