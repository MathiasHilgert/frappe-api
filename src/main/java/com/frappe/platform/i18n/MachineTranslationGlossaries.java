package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Map;
import java.util.UUID;

/**
 * Creates or reuses a machine-translation glossary per business and language pair, so a business's terminology stays
 * consistent across every translation. A module calls {@link #ensure} once it has the terms it wants enforced, then
 * passes the returned reference as {@link TranslationRequest#glossaryReference()} on every later request for that
 * business and pair.
 */
public interface MachineTranslationGlossaries {

    /**
     * Returns the reference of the business's glossary for the given language pair, creating one (or adding the
     * pair to the business's existing glossary) if it does not exist yet, and replacing its entries if it does.
     *
     * @param businessId the business the glossary belongs to
     * @param sourceLanguage the glossary's source language
     * @param targetLanguage the glossary's target language
     * @param entries the glossary's current source-to-target term pairs
     * @return the glossary reference, to pass as {@link TranslationRequest#glossaryReference()}
     * @throws MachineTranslationUnavailableException if no key is configured, or the provider could not be reached
     * @throws MachineTranslationQuotaExceededException if the provider's translation quota is exhausted
     */
    String ensure(UUID businessId, Locale sourceLanguage, Locale targetLanguage, Map<String, String> entries);
}
