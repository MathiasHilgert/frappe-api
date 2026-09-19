package com.frappe.platform.infrastructure.i18n;

import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLClientOptions;
import com.frappe.platform.i18n.MachineTranslationGlossaries;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.MachineTranslator;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Wires {@link MachineTranslator} and {@link MachineTranslationGlossaries} onto the DeepL adapter. No
 * {@code FRAPPE_DEEPL_API_KEY} is a normal condition, not a startup failure: the application still starts, logs one
 * INFO line explaining why translation is unavailable, and every call of both ports fails fast with
 * {@link MachineTranslationUnavailableException} until a key is configured.
 */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(DeepLTranslationProperties.class)
class DeepLTranslationConfiguration {

    private static final Logger log = LoggerFactory.getLogger(DeepLTranslationConfiguration.class);

    /** Creates the configuration; instantiated by Spring. */
    DeepLTranslationConfiguration() {}

    /**
     * The {@link MachineTranslator} port, backed by DeepL when a key is configured.
     *
     * @param properties {@code frappe.translation.deepl.*}
     * @param meters where the characters counter is recorded
     * @param observations where the request observation is recorded
     * @return the adapter
     */
    @Bean
    MachineTranslator machineTranslator(
            DeepLTranslationProperties properties, MeterRegistry meters, ObservationRegistry observations) {
        if (!properties.hasApiKey()) {
            log.atInfo().log("Machine translation unavailable: set FRAPPE_DEEPL_API_KEY to enable it");
        }
        return client(properties)
                .<MachineTranslator>map(
                        deepL -> new DeepLMachineTranslator(deepL, meters, observations, properties.defaultVariants()))
                .orElseGet(() -> new DeepLMachineTranslator(meters, observations));
    }

    /**
     * The {@link MachineTranslationGlossaries} port, backed by DeepL when a key is configured; every call fails fast
     * with {@link MachineTranslationUnavailableException} otherwise.
     *
     * @param properties {@code frappe.translation.deepl.*}
     * @return the adapter
     */
    @Bean
    MachineTranslationGlossaries machineTranslationGlossaries(DeepLTranslationProperties properties) {
        return client(properties)
                .<MachineTranslationGlossaries>map(DeepLGlossaries::new)
                .orElse(DeepLTranslationConfiguration::unavailable);
    }

    // Not a @Bean: each port builds its own client rather than sharing an Optional<DeepLClient> bean, which Spring
    // would otherwise have to treat as a first-class bean type of its own. Building the (stateless, cheap) client
    // twice is simpler than that, and each port's HTTP calls stay independent.
    private static Optional<DeepLClient> client(DeepLTranslationProperties properties) {
        if (!properties.hasApiKey()) {
            return Optional.empty();
        }
        var options = new DeepLClientOptions().setTimeout(properties.timeout()).setMaxRetries(0);
        if (properties.serverUrl() != null) {
            options.setServerUrl(properties.serverUrl());
        }
        return Optional.of(new DeepLClient(properties.apiKey(), options));
    }

    private static String unavailable(UUID businessId, Locale source, Locale target, Map<String, String> entries) {
        throw new MachineTranslationUnavailableException("Machine translation unavailable: no provider key configured");
    }
}
