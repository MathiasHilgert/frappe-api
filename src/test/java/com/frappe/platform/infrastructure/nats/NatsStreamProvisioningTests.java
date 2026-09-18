package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import io.nats.client.Connection;
import io.nats.client.api.RetentionPolicy;
import io.nats.client.api.StreamConfiguration;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;

@SpringBootTest
@Import(TestcontainersConfiguration.class)
class NatsStreamProvisioningTests {

    @Autowired
    Connection connection;

    @Autowired
    NatsStreamProvisioner provisioner;

    @Test
    void createsTheFrappeStreamAtStartup() throws Exception {
        assertMatchesCode(
                connection.jetStreamManagement().getStreamInfo("FRAPPE").getConfiguration());
    }

    @Test
    void restartWithAnExistingDriftedStreamSucceedsAndRestoresTheConfig() throws Exception {
        var jsm = connection.jetStreamManagement();
        var drifted = StreamConfiguration.builder(jsm.getStreamInfo("FRAPPE").getConfiguration())
                .maxAge(Duration.ofDays(1))
                .build();
        jsm.updateStream(drifted);

        provisioner.provision();
        provisioner.provision();

        assertMatchesCode(jsm.getStreamInfo("FRAPPE").getConfiguration());
    }

    private static void assertMatchesCode(StreamConfiguration config) {
        assertThat(config.getSubjects()).containsExactly("frappe.>");
        assertThat(config.getRetentionPolicy()).isEqualTo(RetentionPolicy.Limits);
        assertThat(config.getMaxAge()).isEqualTo(Duration.ofDays(7));
        assertThat(config.getReplicas()).isEqualTo(1);
        assertThat(config.getDuplicateWindow()).isEqualTo(Duration.ofMinutes(10));
    }
}
