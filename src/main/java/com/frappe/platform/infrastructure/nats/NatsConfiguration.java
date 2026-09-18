package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
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
