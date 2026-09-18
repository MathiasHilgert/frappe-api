package com.frappe.platform.infrastructure.nats;

import java.net.URI;
import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.nats.*}, the single source of defaults. They match the compose NATS, so local runs need no
 * configuration; override with environment variables such as {@code FRAPPE_NATS_URL}.
 *
 * @param url server URL
 * @param connectionName name shown in NATS monitoring
 * @param connectionTimeout how long one connection attempt may take; startup never waits longer than this
 * @param reconnectWait pause between connection attempts while NATS is unreachable
 * @param publishTimeout how long a publish waits for the JetStream ack before the publication fails
 */
@ConfigurationProperties("frappe.nats")
record NatsProperties(
        @DefaultValue("nats://localhost:4222") String url,
        @DefaultValue("frappe-api") String connectionName,
        @DefaultValue("2s") Duration connectionTimeout,
        @DefaultValue("2s") Duration reconnectWait,
        @DefaultValue("5s") Duration publishTimeout) {

    private static final int UNDEFINED_PORT = -1;

    /**
     * The URL without credentials, safe for logs and messages.
     *
     * @return {@code scheme://host:port}
     */
    String redactedUrl() {
        var uri = URI.create(url);
        var port = uri.getPort() == UNDEFINED_PORT ? "" : ":" + uri.getPort();
        return uri.getScheme() + "://" + uri.getHost() + port;
    }
}
