package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.resend.core.exception.ResendException;
import com.resend.services.emails.model.CreateEmailOptions;
import com.resend.services.emails.model.CreateEmailResponse;
import java.io.IOException;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class ResendMailTransportTest {

    private static final String FROM = "Frappé <no-reply@frappe.test>";

    private final RenderedMail mail =
            new RenderedMail("Tu código", "<html>Tu código es 123456</html>", "Tu código es 123456", Locale.of("es"));

    private final MailMessage message = MailMessage.of(
                    "mailprobe/welcome", "ana.maria@example.com", Locale.of("es"), Locale.of("en"), Map.of())
            .withIdempotencyKey("welcome/01996a4e-0000-7000-8000-000000000001");

    @Test
    void sendsTheRenderedMailWithTheIdempotencyKey() {
        // Given
        var sent = new AtomicReference<CreateEmailOptions>();
        var idempotencyKey = new AtomicReference<String>();
        var transport = new ResendMailTransport(
                (options, request) -> {
                    sent.set(options);
                    idempotencyKey.set(request.getIdempotencyKey());
                    return new CreateEmailResponse("re_1");
                },
                FROM);

        // When
        transport.deliver(mail, message);

        // Then
        assertThat(sent.get().getFrom()).isEqualTo(FROM);
        assertThat(sent.get().getTo()).containsExactly("ana.maria@example.com");
        assertThat(sent.get().getSubject()).isEqualTo("Tu código");
        assertThat(sent.get().getHtml()).isEqualTo("<html>Tu código es 123456</html>");
        assertThat(sent.get().getText()).isEqualTo("Tu código es 123456");
        assertThat(idempotencyKey.get()).isEqualTo("welcome/01996a4e-0000-7000-8000-000000000001");
    }

    @Test
    void sendsWithoutAnIdempotencyKeyWhenTheMessageHasNone() {
        // Given
        var idempotencyKey = new AtomicReference<String>("unset");
        var transport = new ResendMailTransport(
                (options, request) -> {
                    idempotencyKey.set(request.getIdempotencyKey());
                    return new CreateEmailResponse("re_1");
                },
                FROM);

        // When
        transport.deliver(
                mail,
                MailMessage.of("mailprobe/welcome", "ana@example.com", Locale.of("es"), Locale.of("en"), Map.of()));

        // Then
        assertThat(idempotencyKey.get()).isNull();
    }

    @Test
    void aProviderErrorBecomesOurExceptionWithoutTheProvidersText() {
        // Given Resend answers 503; its error text may echo the request
        var transport = new ResendMailTransport(
                (options, request) -> {
                    throw new ResendException(
                            503,
                            "{\"statusCode\":503,\"name\":\"internal_server_error\","
                                    + "\"message\":\"Could not send to ana.maria@example.com\"}");
                },
                FROM);

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .withMessageContaining("503")
                .withMessageContaining("internal_server_error")
                .withMessageNotContaining("ana.maria")
                .withNoCause();
    }

    @ParameterizedTest
    @ValueSource(ints = {400, 404, 422})
    void aRequestResendRejectsIsPermanent(int status) {
        // Given
        var transport = failingWith(status, "validation_error");

        // When / Then: retrying the same request cannot succeed
        assertThatExceptionOfType(MailRejectedException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .withMessageContaining(String.valueOf(status))
                .withMessageContaining("mailprobe/welcome")
                .withMessageNotContaining("ana.maria")
                .withNoCause();
    }

    @ParameterizedTest
    @ValueSource(ints = {401, 403, 408, 409, 429, 500, 503})
    void outagesLimitsAndFixableSettingsAreTransient(int status) {
        // Given a rate limit, a timeout, a concurrent idempotent request, a server error, or an API key or domain that
        // an operator can fix without losing the mail
        var transport = failingWith(status, "any");

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .isNotInstanceOf(MailRejectedException.class);
    }

    @Test
    void anUnreachableProviderBecomesOurExceptionWithTheNetworkCause() {
        // Given the SDK wraps network failures in a bare RuntimeException
        var transport = new ResendMailTransport(
                (options, request) -> {
                    throw new RuntimeException(new IOException("Failed to connect to api.resend.com"));
                },
                FROM);

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .withMessageContaining("could not be reached")
                .withCauseInstanceOf(IOException.class);
    }

    private static ResendMailTransport failingWith(int status, String name) {
        return new ResendMailTransport(
                (options, request) -> {
                    throw new ResendException(
                            status,
                            "{\"statusCode\":%d,\"name\":\"%s\",\"message\":\"ana.maria@example.com\"}"
                                    .formatted(status, name));
                },
                FROM);
    }

    @Test
    void otherRuntimeFailuresAreBugsAndPassThrough() {
        // Given
        var transport = new ResendMailTransport(
                (options, request) -> {
                    throw new IllegalStateException("bug");
                },
                FROM);

        // When / Then
        assertThatExceptionOfType(IllegalStateException.class).isThrownBy(() -> transport.deliver(mail, message));
    }
}
