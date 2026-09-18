package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import io.nats.client.Connection;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.annotation.AnnotatedElementUtils;
import org.springframework.dao.DataAccessException;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.IncompleteEventPublications;
import org.springframework.modulith.events.RoutingTarget;
import org.springframework.modulith.events.support.EventExternalizerModuleListener;
import tools.jackson.databind.json.JsonMapper;

@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(NatsProperties.class)
class NatsConfiguration {

    private static final Logger log = LoggerFactory.getLogger(NatsConfiguration.class);

    @Bean
    NatsStreamProvisioner natsStreamProvisioner() {
        return new NatsStreamProvisioner();
    }

    /** On every (re)connect: make the stream match the code, then resubmit publications that failed meanwhile. */
    @Bean
    NatsClient natsClient(
            NatsProperties properties,
            NatsStreamProvisioner provisioner,
            EventExternalizationConfiguration externalization,
            ObjectProvider<IncompleteEventPublications> incomplete) {
        return new NatsClient(
                properties, connection -> onConnected(connection, provisioner, externalization, incomplete));
    }

    /**
     * Externalizes every event annotated with {@code @Externalized}; it must implement {@link DomainEvent}, which
     * defines its subject {@code frappe.<module>.<event-kebab>.v<eventVersion>}.
     */
    @Bean
    EventExternalizationConfiguration eventExternalizationConfiguration() {
        return EventExternalizationConfiguration.externalizing()
                .select(EventExternalizationConfiguration.annotatedAsExternalized())
                .routeAll(event -> RoutingTarget.forTarget(subjectOf(event)).withoutKey())
                .build();
    }

    /** Depends on {@link NatsClient}, so Spring destroys it before the connection closes. */
    @Bean
    EventExternalizerModuleListener natsEventExternalizer(
            EventExternalizationConfiguration configuration,
            NatsClient natsClient,
            NatsProperties properties,
            JsonMapper jsonMapper) {
        return new EventExternalizerModuleListener(
                configuration, new NatsEventTransport(natsClient, properties.publishTimeout(), jsonMapper));
    }

    private static void onConnected(
            Connection connection,
            NatsStreamProvisioner provisioner,
            EventExternalizationConfiguration externalization,
            ObjectProvider<IncompleteEventPublications> incomplete) {
        try {
            provisioner.provision(connection);
            incomplete.ifAvailable(publications -> publications.resubmitIncompletePublications(
                    publication -> publication.getStatus() == EventPublication.Status.FAILED
                            && externalization.supports(publication.getEvent())));
        } catch (NatsProvisioningException | DataAccessException e) {
            // Setup runs on a background executor: this is the last place the failure can be reported.
            log.error("NATS connected but setup failed; publications stay incomplete until the next connect", e);
        }
    }

    private static String subjectOf(Object event) {
        if (!(event instanceof DomainEvent domainEvent)) {
            throw new InvalidExternalizedEventException(event.getClass().getName()
                    + " is @Externalized but does not implement " + DomainEvent.class.getName());
        }
        var subject = NatsSubjects.of(domainEvent);
        var annotation = AnnotatedElementUtils.findMergedAnnotation(event.getClass(), Externalized.class);
        if (annotation != null && !annotation.value().isEmpty()) {
            throw new InvalidExternalizedEventException(event.getClass().getName() + " declares @Externalized(\""
                    + annotation.value() + "\"), but the subject is derived (" + subject
                    + "); remove the annotation value");
        }
        return subject;
    }
}
