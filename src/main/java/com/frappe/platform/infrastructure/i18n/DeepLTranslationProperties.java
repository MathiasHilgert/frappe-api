package com.frappe.platform.infrastructure.i18n;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.translation.deepl.*}. {@code apiKey} is blank by default (no {@code FRAPPE_DEEPL_API_KEY} set), which
 * is a normal condition: {@link DeepLTranslationConfiguration} wires an adapter that reports unavailable instead of
 * failing startup. {@code serverUrl} exists only to point contract tests at a stub server.
 *
 * @param apiKey the DeepL account key ({@code FRAPPE_DEEPL_API_KEY}), or blank when translation is not configured
 * @param serverUrl overrides the DeepL API base URL, for tests only; {@code null} selects the real free/pro URL
 *     based on the key
 * @param timeout the bounded timeout for one translation call
 */
@ConfigurationProperties("frappe.translation.deepl")
record DeepLTranslationProperties(
        @DefaultValue("") String apiKey,
        String serverUrl,
        @DefaultValue("10s") Duration timeout) {

    /**
     * Whether a key is configured.
     *
     * @return {@code true} if {@link #apiKey()} is non-blank
     */
    boolean hasApiKey() {
        return !apiKey.isBlank();
    }
}
