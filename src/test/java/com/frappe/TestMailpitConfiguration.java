package com.frappe;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.test.context.DynamicPropertyRegistrar;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.utility.DockerImageName;

/**
 * A fresh Mailpit per Spring test context, matching the compose image: SMTP for the application, the HTTP API for
 * assertions. Spring Boot 4.1 has no Mailpit service connection, so {@code spring.mail.*} is registered here.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TestMailpitConfiguration {

    /** Mailpit's SMTP port inside the container. */
    public static final int SMTP_PORT = 1025;

    /** Mailpit's web UI and API port inside the container. */
    public static final int HTTP_PORT = 8025;

    /**
     * The only recipient domain Mailpit accepts; it refuses every other one with a permanent {@code 550}, like a mailbox
     * that does not exist.
     */
    public static final String ACCEPTED_DOMAIN = "example.com";

    @Bean
    MailpitContainer mailpitContainer() {
        return new MailpitContainer();
    }

    @Bean
    DynamicPropertyRegistrar mailpitProperties(MailpitContainer mailpit) {
        return registry -> {
            registry.add("spring.mail.host", mailpit::getHost);
            registry.add("spring.mail.port", () -> mailpit.getMappedPort(SMTP_PORT));
        };
    }

    /** The Mailpit container, typed so it never competes with other containers for injection. */
    public static final class MailpitContainer extends GenericContainer<MailpitContainer> {

        MailpitContainer() {
            super(DockerImageName.parse("axllent/mailpit:v1.31"));
            withExposedPorts(SMTP_PORT, HTTP_PORT);
            withEnv("MP_SMTP_ALLOWED_RECIPIENTS", "(?i)@example\\.com>?$");
            waitingFor(Wait.forHttp("/readyz").forPort(HTTP_PORT));
        }

        /**
         * The base URL of Mailpit's HTTP API.
         *
         * @return for example {@code http://localhost:32768}
         */
        public String apiUrl() {
            return "http://" + getHost() + ":" + getMappedPort(HTTP_PORT);
        }
    }
}
