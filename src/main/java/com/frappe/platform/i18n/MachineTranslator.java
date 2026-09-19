package com.frappe.platform.i18n;

import java.util.List;

/**
 * Translates text on demand through a machine-translation provider, so modules never depend on the provider
 * directly. Translations are never stored automatically: a caller that persists one marks it {@code origin=MACHINE}
 * (see {@code i18n.md}, "Tenant content").
 *
 * <p>No provider key configured counts as a normal condition, not a startup failure: {@link #isAvailable()} reports
 * {@code false} and the application still starts. Callers check it before offering machine translation, and still
 * handle {@link MachineTranslationUnavailableException} from {@link #translate}, because availability can change
 * between the check and the call.
 */
public interface MachineTranslator {

    /**
     * Reports whether translation can currently be attempted (a provider key is configured). Never throws.
     *
     * @return {@code true} if {@link #translate} may succeed
     */
    boolean isAvailable();

    /**
     * Translates every text of the request from its source language (or the provider's detection) into its target
     * language. One call, no silent retry: a transient provider failure surfaces as
     * {@link MachineTranslationUnavailableException} within a bounded timeout.
     *
     * @param request the batch to translate
     * @return one {@link Translation} per text, in the same order as {@link TranslationRequest#texts()}
     * @throws MachineTranslationUnavailableException if no key is configured, the provider could not be reached, or
     *     did not answer in time
     * @throws MachineTranslationQuotaExceededException if the provider's translation quota is exhausted
     */
    List<Translation> translate(TranslationRequest request);
}
