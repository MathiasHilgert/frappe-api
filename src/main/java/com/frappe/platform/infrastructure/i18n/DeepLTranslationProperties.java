package com.frappe.platform.infrastructure.i18n;

import java.time.Duration;
import java.util.Map;
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
 * @param defaultVariants the region variant DeepL requires for a bare target language that has no default region
 *     (currently {@code en} and {@code pt}: {@code frappe.translation.deepl.default-variants.en=en-US},
 *     {@code .pt=pt-BR}); a target language DeepL accepts bare (e.g. {@code es}) needs no entry
 */
@ConfigurationProperties("frappe.translation.deepl")
record DeepLTranslationProperties(
        @DefaultValue("") String apiKey,
        String serverUrl,
        @DefaultValue("10s") Duration timeout,
        Map<String, String> defaultVariants) {

    /** Applies the default of an empty variant map when Boot did not bind one. */
    DeepLTranslationProperties {
        defaultVariants = defaultVariants == null ? Map.of() : Map.copyOf(defaultVariants);
    }

    /**
     * Whether a key is configured.
     *
     * @return {@code true} if {@link #apiKey()} is non-blank
     */
    boolean hasApiKey() {
        return !apiKey.isBlank();
    }

    /**
     * The key, masked, so it never appears in a log line or a debugger's rendering of this record.
     *
     * @return every field except {@code apiKey}, which is replaced by its presence
     */
    @Override
    public String toString() {
        return "DeepLTranslationProperties[apiKey=%s, serverUrl=%s, timeout=%s, defaultVariants=%s]"
                .formatted(hasApiKey() ? "<configured>" : "<none>", serverUrl, timeout, defaultVariants);
    }
}
