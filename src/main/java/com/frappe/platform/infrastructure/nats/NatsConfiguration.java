package com.frappe.platform.infrastructure.nats;

import io.micrometer.observation.ObservationRegistry;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.support.EventExternalizerModuleListener;
import tools.jackson.databind.json.JsonMapper;

/** Wires the NATS relay: connection, stream setup and the Spring Modulith externalizer. */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(NatsProperties.class)
class NatsConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    NatsConfiguration() {}

    /**
     * Creates or updates the {@code FRAPPE} stream to match the code.
     *
     * @return the provisioner
     */
    @Bean
    NatsStreamProvisioner natsStreamProvisioner() {
        return new NatsStreamProvisioner();
    }

    /**
     * The NATS connection owner; runs {@link NatsConnectSetup} on every (re)connect.
     *
     * @param properties connection settings
     * @param provisioner stream provisioning
     * @param events announces the recovered transport to the outbox recovery
     * @return the client
     */
    @Bean
    NatsClient natsClient(
            NatsProperties properties, NatsStreamProvisioner provisioner, ApplicationEventPublisher events) {
        return new NatsClient(properties, new NatsConnectSetup(provisioner, events));
    }

    /**
     * Externalizes every event annotated with {@code @Externalized}; replaces Modulith's default configuration.
     *
     * @return the selection and routing rules
     */
    @Bean
    EventExternalizationConfiguration eventExternalizationConfiguration() {
        return EventExternalizationConfiguration.externalizing()
                .select(EventExternalizationConfiguration.annotatedAsExternalized())
                .routeAll(ExternalizedEventRouter::route)
                .build();
    }

    /**
     * The Modulith listener publishing through {@link NatsEventTransport}. It depends on {@link NatsClient}, so Spring
     * destroys it before the connection closes.
     *
     * @param configuration selection and routing rules
     * @param natsClient the connection owner
     * @param properties publish timeout
     * @param jsonMapper payload serializer
     * @param observations records every publish
     * @return the externalizer
     */
    @Bean
    EventExternalizerModuleListener natsEventExternalizer(
            EventExternalizationConfiguration configuration,
            NatsClient natsClient,
            NatsProperties properties,
            JsonMapper jsonMapper,
            ObservationRegistry observations) {
        return new EventExternalizerModuleListener(
                configuration,
                new NatsEventTransport(natsClient, properties.publishTimeout(), jsonMapper, observations));
    }
}
