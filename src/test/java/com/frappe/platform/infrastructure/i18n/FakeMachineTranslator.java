package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.MachineTranslator;
import com.frappe.platform.i18n.Translation;
import com.frappe.platform.i18n.TranslationRequest;
import java.util.List;
import java.util.Locale;

/**
 * A deterministic {@link MachineTranslator} for other modules' tests and for local work without a DeepL key: every
 * text comes back tagged with its target language ({@code "[pt] Hola"}), no HTTP call, no billing. The detected
 * source language is the request's source language, or English when the request left it to auto-detection.
 */
public final class FakeMachineTranslator implements MachineTranslator {

    private final boolean available;

    /** Creates an available fake. */
    public FakeMachineTranslator() {
        this(true);
    }

    private FakeMachineTranslator(boolean available) {
        this.available = available;
    }

    /**
     * A fake that behaves like a missing key: {@link #isAvailable()} is {@code false} and {@link #translate} throws,
     * for tests of the "no key configured" path without a DeepL adapter.
     *
     * @return the unavailable fake
     */
    public static FakeMachineTranslator unavailable() {
        return new FakeMachineTranslator(false);
    }

    @Override
    public boolean isAvailable() {
        return available;
    }

    @Override
    public List<Translation> translate(TranslationRequest request) {
        if (!available) {
            throw new MachineTranslationUnavailableException("Fake machine translator is unavailable");
        }
        var detected = request.sourceLanguage() != null ? request.sourceLanguage() : Locale.of("en");
        var tag = request.targetLanguage().toLanguageTag();
        return request.texts().stream()
                .map(text -> new Translation("[" + tag + "] " + text, detected))
                .toList();
    }
}
