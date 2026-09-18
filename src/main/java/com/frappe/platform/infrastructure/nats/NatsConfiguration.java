package com.frappe.platform.infrastructure.nats;

import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.IncompleteEventPublications;
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
     * @param externalization selects NATS publications for resubmission
     * @param incompletePublications registry resubmission, looked up lazily
     * @return the client
     */
    @Bean
    NatsClient natsClient(
            NatsProperties properties,
            NatsStreamProvisioner provisioner,
            EventExternalizationConfiguration externalization,
            ObjectProvider<IncompleteEventPublications> incompletePublications) {
        return new NatsClient(properties, new NatsConnectSetup(provisioner, externalization, incompletePublications));
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
     * @return the externalizer
     */
    @Bean
    EventExternalizerModuleListener natsEventExternalizer(
            EventExternalizationConfiguration configuration,
            NatsClient natsClient,
            NatsProperties properties,
            JsonMapper jsonMapper) {
        return new EventExternalizerModuleListener(
                configuration, new NatsEventTransport(natsClient, properties.publishTimeout(), jsonMapper));
    }
}
