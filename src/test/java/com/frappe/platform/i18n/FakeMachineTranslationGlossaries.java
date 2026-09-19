package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * A deterministic {@link MachineTranslationGlossaries} for other modules' tests: {@link #ensure} returns a stable,
 * predictable reference per business and language pair, no HTTP call.
 */
public final class FakeMachineTranslationGlossaries implements MachineTranslationGlossaries {

    private final Map<String, String> references = new ConcurrentHashMap<>();

    @Override
    public String ensure(UUID businessId, Locale sourceLanguage, Locale targetLanguage, Map<String, String> entries) {
        var key = businessId + ":" + sourceLanguage.getLanguage() + ":" + targetLanguage.getLanguage();
        return references.computeIfAbsent(key, ignored -> "fake-glossary-" + UUID.randomUUID());
    }
}
