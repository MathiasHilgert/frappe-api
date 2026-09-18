package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import io.nats.client.Connection;
import io.nats.client.JetStreamOptions;
import io.nats.client.Nats;
import io.nats.client.Options;
import java.io.IOException;
import java.time.Duration;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeoutException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.RoutingTarget;
import org.springframework.modulith.events.support.EventExternalizerModuleListener;
import tools.jackson.databind.json.JsonMapper;

@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(NatsProperties.class)
class NatsConfiguration {

    private static final Logger log = LoggerFactory.getLogger(NatsConfiguration.class);
    private static final Duration DRAIN_TIMEOUT = Duration.ofSeconds(10);

    @Bean(destroyMethod = "")
    Connection natsConnection(NatsProperties properties) throws InterruptedException {
        var options = Options.builder()
                .server(properties.url())
                .connectionName(properties.connectionName())
                .connectionTimeout(properties.connectionTimeout())
                .maxReconnects(-1)
                .connectionListener((connection, event) -> log.info("NATS {}", event))
                .build();
        try {
            var connection = Nats.connect(options);
            log.info("Connected to NATS at {}", properties.url());
            return connection;
        } catch (IOException e) {
            throw new IllegalStateException(
                    "Cannot connect to NATS at " + properties.url()
                            + "; start it with 'docker compose up -d nats' or set FRAPPE_NATS_URL",
                    e);
        }
    }

    @Bean
    NatsConnectionCloser natsConnectionCloser(Connection natsConnection) {
        return new NatsConnectionCloser(natsConnection);
    }

    @Bean
    NatsStreamProvisioner natsStreamProvisioner(Connection natsConnection) throws IOException {
        var provisioner = new NatsStreamProvisioner(natsConnection.jetStreamManagement());
        provisioner.provision();
        return provisioner;
    }

    /**
     * Externalizes every event annotated with {@code @Externalized}; it must implement {@link DomainEvent}, which
     * defines its subject {@code frappe.<module>.<event-kebab>.v<eventVersion>}.
     */
    @Bean
    EventExternalizationConfiguration eventExternalizationConfiguration() {
        return EventExternalizationConfiguration.externalizing()
                .select(EventExternalizationConfiguration.annotatedAsExternalized())
                .routeAll(event -> RoutingTarget.forTarget(NatsSubjects.of(requireDomainEvent(event)))
                        .withoutKey())
                .build();
    }

    @Bean
    EventExternalizerModuleListener natsEventExternalizer(
            EventExternalizationConfiguration configuration,
            Connection natsConnection,
            NatsProperties properties,
            JsonMapper jsonMapper)
            throws IOException {
        var jetStream = natsConnection.jetStream(JetStreamOptions.builder()
                .requestTimeout(properties.publishTimeout())
                .build());
        return new EventExternalizerModuleListener(configuration, new NatsEventTransport(jetStream, jsonMapper));
    }

    private static DomainEvent requireDomainEvent(Object event) {
        if (event instanceof DomainEvent domainEvent) {
            return domainEvent;
        }
        throw new IllegalStateException(
                event.getClass().getName() + " is @Externalized but does not implement " + DomainEvent.class.getName());
    }

    /** Drains in-flight messages, then closes, when the context shuts down. */
    record NatsConnectionCloser(Connection connection) implements AutoCloseable {

        @Override
        public void close() throws InterruptedException {
            try {
                connection.drain(DRAIN_TIMEOUT).get();
            } catch (TimeoutException | ExecutionException | IllegalStateException e) {
                log.warn("NATS drain did not finish cleanly; closing", e);
                connection.close();
            }
        }
    }
}
