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
import com.frappe.platform.i18n.MachineTranslationGlossaries;
import com.frappe.platform.i18n.MachineTranslationQuotaExceededException;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * {@link MachineTranslationGlossaries} over the DeepL multilingual glossary API (v3): one glossary per business,
 * named {@code frappe-business-<businessId>}, one dictionary per language pair inside it. DeepL has no
 * "get or create" endpoint, so {@link #ensure} lists the account's glossaries and reuses (or extends) a matching one
 * instead of creating a duplicate on every call.
 *
 * <p>Two instances (two application nodes) can race and both create a business's first glossary at once; the loser's
 * extra glossary is harmless but wasteful, so after a create this class re-lists, keeps the oldest same-name
 * glossary and deletes the rest. The chosen reference is cached per business and language pair for this instance's
 * lifetime, so a repeated {@link #ensure} call for the same pair never lists the account again.
 *
 * <p>Provider failures are sanitized the same way as {@link DeepLMachineTranslator}: mapped to our own exceptions
 * with the provider failure only as {@linkplain Throwable#getCause() cause}, never logged here (see its javadoc).
 */
final class DeepLGlossaries implements MachineTranslationGlossaries {

    private final DeepLClient client;

    private final Map<CacheKey, String> cache = new ConcurrentHashMap<>();

    /**
     * Creates the glossary manager.
     *
     * @param client the DeepL client
     */
    DeepLGlossaries(DeepLClient client) {
        this.client = client;
    }

    @Override
    public String ensure(UUID businessId, Locale sourceLanguage, Locale targetLanguage, Map<String, String> entries) {
        var key = new CacheKey(businessId, sourceLanguage.getLanguage(), targetLanguage.getLanguage());
        var cached = cache.get(key);
        if (cached != null) {
            return cached;
        }
        var name = glossaryName(businessId);
        var glossaryEntries = new GlossaryEntries(entries);
        var reference = oldestDeduped(name)
                .map(existing -> {
                    replaceDictionary(existing.getGlossaryId(), sourceLanguage, targetLanguage, glossaryEntries);
                    return existing.getGlossaryId();
                })
                .orElseGet(() -> {
                    var created = create(name, sourceLanguage, targetLanguage, glossaryEntries);
                    return oldestDeduped(name)
                            .map(MultilingualGlossaryInfo::getGlossaryId)
                            .orElseGet(created::getGlossaryId);
                });
        cache.put(key, reference);
        return reference;
    }

    /**
     * The name that groups every glossary of one business, so a name collision with an unrelated glossary a human
     * created directly in the DeepL account is not possible.
     */
    private static String glossaryName(UUID businessId) {
        return "frappe-business-" + businessId;
    }

    /**
     * Lists the account's glossaries matching {@code name}, deletes every one but the oldest (a concurrent duplicate),
     * and returns that oldest one, if any.
     */
    private Optional<MultilingualGlossaryInfo> oldestDeduped(String name) {
        var matching = list().stream()
                .filter(glossary -> glossary.getName().equals(name))
                .sorted(Comparator.comparing(MultilingualGlossaryInfo::getCreationTime))
                .toList();
        matching.stream().skip(1).forEach(this::deleteBestEffort);
        return matching.stream().findFirst();
    }

    // Cleans up a duplicate created by a losing concurrent instance; the surviving oldest glossary is already
    // correct, so a failed delete here (network blip, already gone) must not fail ensure() for this instance.
    private void deleteBestEffort(MultilingualGlossaryInfo duplicate) {
        try {
            client.deleteMultilingualGlossary(duplicate.getGlossaryId());
        } catch (DeepLException e) {
            // best effort: ignored, see method comment
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
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
        } catch (TooManyRequestsException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider rate limited the request", e);
        } catch (ConnectionException e) {
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
                sourceLanguage.getLanguage(), targetLanguage.getLanguage(), entries);
        try {
            return client.createMultilingualGlossary(name, List.of(dictionary));
        } catch (AuthorizationException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider rate limited the request", e);
        } catch (ConnectionException e) {
            throw new MachineTranslationUnavailableException("Machine translation provider did not answer in time", e);
        } catch (DeepLException | IllegalArgumentException e) {
            throw new MachineTranslationUnavailableException("Creating a machine translation glossary failed", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MachineTranslationUnavailableException("Interrupted while waiting for machine translation", e);
        }
    }

    private void replaceDictionary(
            String glossaryId, Locale sourceLanguage, Locale targetLanguage, GlossaryEntries entries) {
        try {
            client.replaceMultilingualGlossaryDictionary(
                    glossaryId, sourceLanguage.getLanguage(), targetLanguage.getLanguage(), entries);
        } catch (AuthorizationException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider rate limited the request", e);
        } catch (ConnectionException e) {
            throw new MachineTranslationUnavailableException("Machine translation provider did not answer in time", e);
        } catch (DeepLException | IllegalArgumentException e) {
            throw new MachineTranslationUnavailableException("Updating a machine translation glossary failed", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MachineTranslationUnavailableException("Interrupted while waiting for machine translation", e);
        }
    }

    private record CacheKey(UUID businessId, String sourceLanguage, String targetLanguage) {}
}
