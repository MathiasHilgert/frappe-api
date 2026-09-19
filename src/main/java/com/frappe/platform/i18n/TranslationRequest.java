package com.frappe.platform.i18n;

import java.util.List;
import java.util.Locale;
import java.util.Objects;

/**
 * A batch of texts to machine-translate from one language to another. Every text is translated with the same source,
 * target, glossary and formality.
 *
 * @param texts the texts to translate, in order; none may be blank
 * @param sourceLanguage the source language, or {@code null} to let the provider detect it per text
 * @param targetLanguage the language to translate into
 * @param glossaryReference an existing provider glossary to apply, or {@code null} for none; requires
 *     {@code sourceLanguage} (the provider cannot apply a glossary while auto-detecting the source)
 * @param formality the desired formality; {@link TranslationFormality#DEFAULT} when the caller has no preference
 */
public record TranslationRequest(
        List<String> texts,
        Locale sourceLanguage,
        Locale targetLanguage,
        String glossaryReference,
        TranslationFormality formality) {

    /** Validates the request. */
    public TranslationRequest {
        Objects.requireNonNull(texts, "texts");
        if (texts.isEmpty()) {
            throw new IllegalArgumentException("texts must not be empty");
        }
        for (var text : texts) {
            if (text == null || text.isBlank()) {
                throw new IllegalArgumentException("texts must not contain a blank text");
            }
        }
        Objects.requireNonNull(targetLanguage, "targetLanguage");
        Objects.requireNonNull(formality, "formality");
        if (glossaryReference != null && sourceLanguage == null) {
            throw new IllegalArgumentException("glossaryReference requires a sourceLanguage");
        }
        texts = List.copyOf(texts);
    }

    /**
     * Creates a request without a glossary and the provider's default formality.
     *
     * @param texts the texts to translate
     * @param sourceLanguage the source language, or {@code null} for auto-detection
     * @param targetLanguage the target language
     * @return the request
     */
    public static TranslationRequest of(List<String> texts, Locale sourceLanguage, Locale targetLanguage) {
        return new TranslationRequest(texts, sourceLanguage, targetLanguage, null, TranslationFormality.DEFAULT);
    }
}
