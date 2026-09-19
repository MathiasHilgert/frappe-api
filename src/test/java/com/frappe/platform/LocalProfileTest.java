package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;

import java.io.IOException;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.core.env.MapPropertySource;
import org.springframework.core.env.PropertiesPropertySource;
import org.springframework.core.env.StandardEnvironment;
import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.support.PropertiesLoaderUtils;

/** The {@code local} profile points at compose.yaml's services, also when their host ports were moved. */
class LocalProfileTest {

    @Test
    void followsTheComposeHostPortOverrides() throws IOException {
        // Given
        var environment = localProfile(Map.of(
                "FRAPPE_POSTGRES_PORT", "15432",
                "FRAPPE_NATS_PORT", "14222",
                "FRAPPE_VALKEY_PORT", "16379",
                "FRAPPE_MAILPIT_SMTP_PORT", "11025"));

        // Then
        assertThat(environment.getProperty("FRAPPE_DB_URL")).isEqualTo("jdbc:postgresql://localhost:15432/frappe");
        assertThat(environment.getProperty("frappe.nats.url")).isEqualTo("nats://localhost:14222");
        assertThat(environment.getProperty("FRAPPE_VALKEY_URL")).isEqualTo("redis://localhost:16379");
        assertThat(environment.getProperty("spring.mail.port")).isEqualTo("11025");
    }

    @Test
    void usesTheStandardPortsByDefault() throws IOException {
        // Given
        var environment = localProfile(Map.of());

        // Then
        assertThat(environment.getProperty("FRAPPE_DB_URL")).isEqualTo("jdbc:postgresql://localhost:5432/frappe");
        assertThat(environment.getProperty("frappe.nats.url")).isEqualTo("nats://localhost:4222");
        assertThat(environment.getProperty("FRAPPE_VALKEY_URL")).isEqualTo("redis://localhost:6379");
        assertThat(environment.getProperty("FRAPPE_SECRET_PEPPER")).hasSizeGreaterThanOrEqualTo(32);
        assertThat(environment.getProperty("FRAPPE_DIGEST_PEPPER"))
                .hasSizeGreaterThanOrEqualTo(32)
                .isNotEqualTo(environment.getProperty("FRAPPE_SECRET_PEPPER"));
    }

    @Test
    void sendsMailToMailpitOverSmtp() throws IOException {
        // Given
        var environment = localProfile(Map.of());

        // Then no Resend key is needed locally
        assertThat(environment.getProperty("frappe.mail.provider")).isEqualTo("smtp");
        assertThat(environment.getProperty("spring.mail.host")).isEqualTo("localhost");
        assertThat(environment.getProperty("spring.mail.port")).isEqualTo("1025");
        assertThat(environment.getProperty("FRAPPE_MAIL_FROM")).isNotBlank();
    }

    private static StandardEnvironment localProfile(Map<String, Object> variables) throws IOException {
        var environment = new StandardEnvironment();
        var sources = environment.getPropertySources();
        sources.addFirst(new PropertiesPropertySource(
                "local", PropertiesLoaderUtils.loadProperties(new ClassPathResource("application-local.properties"))));
        sources.addFirst(new MapPropertySource("variables", variables));
        return environment;
    }
}
