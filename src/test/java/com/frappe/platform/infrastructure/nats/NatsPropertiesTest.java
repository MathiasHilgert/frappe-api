package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Duration;
import org.junit.jupiter.api.Test;

class NatsPropertiesTest {

    private static final Duration ANY = Duration.ofSeconds(1);

    @Test
    void redactsCredentialsFromTheLoggableUrl() {
        // Given a URL carrying user and password
        var properties = new NatsProperties("nats://frappe:s3cret@nats.internal:4222", "test", ANY, ANY, ANY);

        // When / Then only scheme, host and port remain
        assertThat(properties.redactedUrl()).isEqualTo("nats://nats.internal:4222");
    }

    @Test
    void keepsAUrlWithoutCredentials() {
        // Given a plain URL
        var properties = new NatsProperties("nats://localhost:4222", "test", ANY, ANY, ANY);

        // When / Then it is unchanged
        assertThat(properties.redactedUrl()).isEqualTo("nats://localhost:4222");
    }
}
