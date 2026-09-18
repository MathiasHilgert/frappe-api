package com.frappe;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.test.context.DynamicPropertyRegistrar;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.utility.DockerImageName;

/**
 * A fresh, non-reused NATS JetStream per Spring test context: tests may pause it or change the stream without
 * affecting other contexts or worktrees. It starts in well under a second.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TestNatsConfiguration {

    public static final int NATS_PORT = 4222;

    @Bean
    GenericContainer<?> natsContainer() {
        return natsJetStream();
    }

    @Bean
    DynamicPropertyRegistrar natsProperties(GenericContainer<?> natsContainer) {
        return registry -> registry.add("frappe.nats.url", () -> natsUrl(natsContainer));
    }

    /** A NATS server with JetStream, matching the compose image. */
    public static GenericContainer<?> natsJetStream() {
        return new GenericContainer<>(DockerImageName.parse("nats:2.12-alpine"))
                .withCommand("-js")
                .withExposedPorts(NATS_PORT)
                .waitingFor(Wait.forLogMessage(".*Server is ready.*", 1));
    }

    public static String natsUrl(GenericContainer<?> nats) {
        return "nats://" + nats.getHost() + ":" + nats.getMappedPort(NATS_PORT);
    }
}
