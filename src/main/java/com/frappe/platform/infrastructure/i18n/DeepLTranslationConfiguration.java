package com.frappe.platform.infrastructure.i18n;

import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLClientOptions;
import com.frappe.platform.i18n.MachineTranslator;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Wires {@link MachineTranslator} onto the DeepL adapter. No {@code FRAPPE_DEEPL_API_KEY} is a normal condition, not
 * a startup failure: the application still starts, logs one INFO line explaining why translation is unavailable, and
 * every call fails fast with {@code MachineTranslationUnavailableException} until a key is configured.
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
            return new DeepLMachineTranslator(meters, observations);
        }
        var options = new DeepLClientOptions().setTimeout(properties.timeout()).setMaxRetries(0);
        if (properties.serverUrl() != null) {
            options.setServerUrl(properties.serverUrl());
        }
        return new DeepLMachineTranslator(new DeepLClient(properties.apiKey(), options), meters, observations);
    }
}
