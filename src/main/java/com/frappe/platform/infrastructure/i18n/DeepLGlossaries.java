package com.frappe.platform.infrastructure.i18n;

import com.deepl.api.AuthorizationException;
import com.deepl.api.ConnectionException;
import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLException;
import com.deepl.api.GlossaryEntries;
import com.deepl.api.MultilingualGlossaryDictionaryEntries;
import com.deepl.api.MultilingualGlossaryInfo;
import com.deepl.api.QuotaExceededException;
import com.deepl.api.TooManyRequestsException;
import com.frappe.platform.i18n.MachineTranslationQuotaExceededException;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Creates or reuses one DeepL glossary per business and language pair. A business's glossary is named after its id,
 * so {@link #ensure} first lists the account's glossaries and reuses a matching one instead of creating a duplicate
 * on every call (DeepL has no "get or create" endpoint).
 *
 * <p>Not part of {@link com.frappe.platform.i18n.MachineTranslator}: catalog and menu (later tickets) call this when
 * they decide a business needs a glossary, then pass the returned id as {@link com.frappe.platform.i18n.TranslationRequest#glossaryReference()}.
 *
 * <p>Provider failures are sanitized and logged once at ERROR before rethrow, exactly like {@link DeepLMachineTranslator}: the
 * provider's name and response never reach a caller, only our own exceptions and the log line's structured fields.
 */
final class DeepLGlossaries {

    private static final Logger log = LoggerFactory.getLogger(DeepLGlossaries.class);

    private final DeepLClient client;

    /**
     * Creates the glossary manager.
     *
     * @param client the DeepL client
     */
    DeepLGlossaries(DeepLClient client) {
        this.client = client;
    }

    /**
     * Returns the id of the business's glossary for the given language pair, creating one if none exists yet.
     *
     * @param businessId the business the glossary belongs to
     * @param sourceLanguage the glossary dictionary's source language
     * @param targetLanguage the glossary dictionary's target language
     * @param entries the entries to create the glossary with, if it does not exist yet
     * @return the glossary id, to pass as {@link com.frappe.platform.i18n.TranslationRequest#glossaryReference()}
     * @throws MachineTranslationUnavailableException if the provider could not be reached or refused the key
     * @throws MachineTranslationQuotaExceededException if the account's translation quota is exhausted
     */
    String ensure(UUID businessId, Locale sourceLanguage, Locale targetLanguage, GlossaryEntries entries) {
        try {
            var name = businessId.toString();
            var existing = list().stream()
                    .filter(glossary -> glossary.getName().equals(name))
                    .filter(glossary -> matches(glossary, sourceLanguage, targetLanguage))
                    .findFirst();
            if (existing.isPresent()) {
                return existing.get().getGlossaryId();
            }
            return create(name, sourceLanguage, targetLanguage, entries).getGlossaryId();
        } catch (RuntimeException e) {
            if (e.getCause() != null) {
                log.atError()
                        .addKeyValue(LogFields.TARGET_LANGUAGE, targetLanguage.toLanguageTag())
                        .setCause(e.getCause())
                        .log("Ensuring a machine translation glossary failed");
            }
            throw e;
        }
    }

    private List<MultilingualGlossaryInfo> list() {
        try {
            return client.listMultilingualGlossaries();
        } catch (AuthorizationException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException | ConnectionException e) {
            throw new MachineTranslationUnavailableException("Machine translation provider did not answer in time", e);
        } catch (DeepLException e) {
            throw new MachineTranslationUnavailableException("Listing machine translation glossaries failed", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MachineTranslationUnavailableException("Interrupted while waiting for machine translation", e);
        }
    }

    private MultilingualGlossaryInfo create(
            String name, Locale sourceLanguage, Locale targetLanguage, GlossaryEntries entries) {
        var dictionary = new MultilingualGlossaryDictionaryEntries(
                sourceLanguage.toLanguageTag(), targetLanguage.toLanguageTag(), entries);
        try {
            return client.createMultilingualGlossary(name, List.of(dictionary));
        } catch (AuthorizationException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException | ConnectionException e) {
            throw new MachineTranslationUnavailableException("Machine translation provider did not answer in time", e);
        } catch (DeepLException | IllegalArgumentException e) {
            throw new MachineTranslationUnavailableException("Creating a machine translation glossary failed", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MachineTranslationUnavailableException("Interrupted while waiting for machine translation", e);
        }
    }

    private static boolean matches(MultilingualGlossaryInfo glossary, Locale sourceLanguage, Locale targetLanguage) {
        return glossary.getDictionaries().stream()
                .anyMatch(dictionary ->
                        dictionary.getSourceLanguageCode().equalsIgnoreCase(sourceLanguage.toLanguageTag())
                                && dictionary.getTargetLanguageCode().equalsIgnoreCase(targetLanguage.toLanguageTag()));
    }
}
