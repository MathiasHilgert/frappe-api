package com.frappe.platform.infrastructure.nats;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.nats.*}; defaults match the compose NATS so local runs need no configuration.
 *
 * @param url server URL, env {@code FRAPPE_NATS_URL}
 * @param connectionName name shown in NATS monitoring
 * @param connectionTimeout how long startup waits for the initial connection
 * @param publishTimeout how long a publish waits for the JetStream ack before the publication fails
 */
@ConfigurationProperties("frappe.nats")
record NatsProperties(
        @DefaultValue("nats://localhost:4222") String url,
        @DefaultValue("frappe-api") String connectionName,
        @DefaultValue("5s") Duration connectionTimeout,
        @DefaultValue("5s") Duration publishTimeout) {}
