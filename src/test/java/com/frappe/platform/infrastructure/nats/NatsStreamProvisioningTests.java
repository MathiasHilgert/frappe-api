package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import io.nats.client.JetStreamManagement;
import io.nats.client.api.RetentionPolicy;
import io.nats.client.api.StreamConfiguration;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class NatsStreamProvisioningTests {

    @Autowired
    NatsClient client;

    @Autowired
    NatsStreamProvisioner provisioner;

    @Test
    void createsTheFrappeStreamOnConnect() throws Exception {
        // When / Then
        assertMatchesCode(awaitStream().getConfiguration());
    }

    @Test
    void restartWithAnExistingDriftedStreamSucceedsAndRestoresTheConfig() throws Exception {
        // Given
        var jsm = jsm();
        var drifted = StreamConfiguration.builder(awaitStream().getConfiguration())
                .maxAge(Duration.ofDays(1))
                .build();
        jsm.updateStream(drifted);

        // When
        // Provisioning runs on every reconnect, so it must be idempotent: running it twice changes nothing more.
        provisioner.provision(client.connection());
        provisioner.provision(client.connection());

        // Then
        assertMatchesCode(jsm.getStreamInfo("FRAPPE").getConfiguration());
    }

    private io.nats.client.api.StreamInfo awaitStream() throws Exception {
        var jsm = jsm();
        await().ignoreExceptions().until(() -> jsm.getStreamInfo("FRAPPE") != null);
        return jsm.getStreamInfo("FRAPPE");
    }

    private JetStreamManagement jsm() throws Exception {
        return client.connection().jetStreamManagement();
    }

    private static void assertMatchesCode(StreamConfiguration config) {
        assertThat(config.getSubjects()).containsExactly("frappe.>");
        assertThat(config.getRetentionPolicy()).isEqualTo(RetentionPolicy.Limits);
        assertThat(config.getMaxAge()).isEqualTo(Duration.ofDays(7));
        assertThat(config.getReplicas()).isEqualTo(1);
        assertThat(config.getDuplicateWindow()).isEqualTo(Duration.ofMinutes(10));
    }
}
