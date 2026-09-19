package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestMailpitConfiguration;
import com.frappe.TestMailpitConfiguration.MailpitContainer;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.mail.MailMessage;
import com.frappe.platform.mail.Mailer;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.web.client.RestClient;
import tools.jackson.databind.JsonNode;

/**
 * The local setup end to end: the {@code local} profile sends over SMTP to Mailpit (the compose image), rendered from
 * the real catalogs by the application message source.
 */
@SpringBootTest
@ActiveProfiles("local")
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestMailpitConfiguration.class})
class MailpitIntegrationTests {

    @Autowired
    Mailer mailer;

    @Autowired
    MailpitContainer mailpit;

    @Autowired
    MeterRegistry meters;

    @Test
    void aSentMailAppearsInMailpitEntirelyInItsLanguage() {
        // Given
        var recipient = "ana-" + UUID.randomUUID() + "@example.com";

        // When
        mailer.send(MailMessage.of(
                "mailprobe/welcome",
                recipient,
                Locale.of("es"),
                Locale.of("en"),
                Map.of("name", "Ana", "code", "123456")));

        // Then
        var api = RestClient.create(mailpit.apiUrl());
        var summary =
                await().atMost(Duration.ofSeconds(10)).until(() -> messageTo(api, recipient), node -> node != null);
        assertThat(summary.get("Subject").asString()).isEqualTo("Tu código de Frappé");
        // application-local.properties is read as ISO-8859-1: the sender's display name must survive it
        assertThat(summary.get("From").get("Name").asString()).isEqualTo("Frappé");
        var message = api.get()
                .uri("/api/v1/message/{id}", summary.get("ID").asString())
                .retrieve()
                .body(JsonNode.class);
        assertThat(message.get("HTML").asString())
                .contains("lang=\"es\"", "¡Hola, Ana!", "Tu código es 123456.")
                .doesNotContain("Hello", "Your code");
        // SMTP sends CRLF line breaks
        assertThat(message.get("Text").asString().replace("\r\n", "\n"))
                .isEqualTo("¡Hola, Ana!\n\nTu código es 123456. Vence en 15 minutos.")
                .doesNotContain("<");
    }

    @Test
    void aRecipientTheServerRefusesIsRejectedOnceAndNotRetried() {
        // Given Mailpit answers 550 for any domain but example.com
        var recipient = "ana-" + UUID.randomUUID() + "@refused.test";
        var rejectedBefore = rejected();

        // When: returns normally, so the calling listener completes and the outbox does not retry
        mailer.send(MailMessage.of(
                "mailprobe/welcome", recipient, Locale.of("es"), Locale.of("en"), Map.of("name", "Ana", "code", "1")));

        // Then
        assertThat(rejected()).isEqualTo(rejectedBefore + 1);
        assertThat(messageTo(RestClient.create(mailpit.apiUrl()), recipient)).isNull();
    }

    private long rejected() {
        return meters.find("mail.send").tag("mail.outcome", "rejected").timers().stream()
                .mapToLong(timer -> timer.count())
                .sum();
    }

    private static JsonNode messageTo(RestClient api, String recipient) {
        var found = api.get()
                .uri(uri -> uri.path("/api/v1/search")
                        .queryParam("query", "to:" + recipient)
                        .build())
                .retrieve()
                .body(JsonNode.class);
        var messages = found.get("messages");
        return messages.isEmpty() ? null : messages.get(0);
    }
}
