package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.assertj.core.api.Assertions.catchThrowable;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.resend.core.exception.ResendException;
import gg.jte.ContentType;
import gg.jte.TemplateEngine;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationHandler;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;
import org.slf4j.LoggerFactory;
import org.springframework.context.support.StaticMessageSource;
import org.springframework.mail.javamail.JavaMailSenderImpl;

class ObservedMailerTest {

    private static final String RECIPIENT = "ana.maria@example.com";

    private static final String CODE = "482913";

    private static final String API_KEY = "re_live_not_a_real_key_0123456789";

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final SimpleMeterRegistry meters = new SimpleMeterRegistry();

    private final StaticMessageSource catalogs = new StaticMessageSource();

    private final MailRenderer renderer =
            new MailRenderer(TemplateEngine.createPrecompiled(ContentType.Html), catalogs, meters);

    private final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    private final Logger root = (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);

    private Level rootLevel;

    ObservedMailerTest() {
        catalogs.addMessage("mailprobe.mail.welcome.subject", Locale.ENGLISH, "Your code");
        catalogs.addMessage("mailprobe.mail.welcome.greeting", Locale.ENGLISH, "Hello, {0}!");
        catalogs.addMessage("mailprobe.mail.welcome.code", Locale.ENGLISH, "Your code is {0}.");
    }

    private final List<Observation.Context> stopped = new ArrayList<>();

    @BeforeEach
    void captureObservationsAndLogs() {
        observations.observationConfig().observationHandler(new ObservationHandler<>() {
            @Override
            public boolean supportsContext(Observation.Context context) {
                return true;
            }

            @Override
            public void onStop(Observation.Context context) {
                stopped.add(context);
            }
        });
        rootLevel = root.getLevel();
        root.setLevel(Level.DEBUG);
        logs.start();
        root.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        root.detachAppender(logs);
        root.setLevel(rootLevel);
    }

    @Test
    void observesASuccessfulSend() {
        // Given
        var delivered = new ArrayList<RenderedMail>();
        var mailer =
                new ObservedMailer(renderer, transport("resend", (mail, message) -> delivered.add(mail)), observations);

        // When
        mailer.send(welcome());

        // Then
        assertThat(delivered)
                .singleElement()
                .satisfies(mail -> assertThat(mail.html()).contains(CODE));
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("mail.send")
                .hasLowCardinalityKeyValue("mail.provider", "resend")
                .hasLowCardinalityKeyValue("mail.template", "mailprobe/welcome")
                .hasLowCardinalityKeyValue("mail.locale", "en")
                .hasLowCardinalityKeyValue("mail.outcome", "sent")
                .doesNotHaveError()
                .hasBeenStopped();
    }

    @Test
    void aTransientFailureIsObservedAndRethrownWithoutLogging() {
        // Given
        var mailer = new ObservedMailer(
                renderer,
                transport("resend", (mail, message) -> {
                    throw new MailDeliveryException("Sending mail mailprobe/welcome through resend failed: HTTP 503");
                }),
                observations);

        // When
        assertThatExceptionOfType(MailDeliveryException.class).isThrownBy(() -> mailer.send(welcome()));

        // Then the listener boundary logs it once (log or rethrow, never both); the outbox retries it
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("mail.send")
                .hasLowCardinalityKeyValue("mail.outcome", "failed")
                .hasError()
                .hasBeenStopped();
        assertThat(logs.list).noneMatch(event -> event.getLevel().isGreaterOrEqual(Level.WARN));
    }

    @Test
    void aPermanentRejectionIsLoggedOnceCountedAndNotRetried() {
        // Given
        var mailer = new ObservedMailer(
                renderer,
                transport("resend", (mail, message) -> {
                    throw new MailRejectedException(
                            "Resend rejected mail mailprobe/welcome: HTTP 422 (validation_error)");
                }),
                observations);

        // When: returns normally, so the listener completes and the outbox does not retry what cannot succeed
        mailer.send(welcome());

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("mail.send")
                .hasLowCardinalityKeyValue("mail.outcome", "rejected")
                .hasBeenStopped();
        assertThat(logs.list)
                .filteredOn(event -> event.getLevel() == Level.ERROR)
                .singleElement()
                .satisfies(event -> assertThat(keyValues(event))
                        .contains(
                                "frappe.mail.template=mailprobe/welcome",
                                "frappe.mail.provider=resend",
                                "frappe.mail.locale=en"));
    }

    static Stream<MailTransport> failingTransports() {
        var smtp = new JavaMailSenderImpl();
        smtp.setHost("localhost");
        smtp.setPort(TestSockets.closedPort());
        return Stream.of(
                new ResendMailTransport(
                        (options, request) -> {
                            throw new ResendException(
                                    422,
                                    "{\"statusCode\":422,\"name\":\"validation_error\",\"message\":\"Invalid `to`: "
                                            + RECIPIENT + " (key " + API_KEY + ")\"}");
                        },
                        "Frappé <no-reply@frappe.test>"),
                new SmtpMailTransport(smtp, "Frappé <no-reply@frappe.test>"));
    }

    @ParameterizedTest
    @MethodSource("failingTransports")
    void noAdapterPutsTheBodyTheApiKeyOrTheRecipientIntoLogsOrSpans(MailTransport transport) {
        // Given
        var mailer = new ObservedMailer(renderer, transport, observations);

        // When the Resend adapter rejects (422) and the SMTP one fails (no server)
        catchThrowable(() -> mailer.send(welcome()));

        // Then
        var recorded = new ArrayList<String>();
        logs.list.forEach(event -> {
            recorded.add(event.getFormattedMessage());
            recorded.addAll(keyValues(event));
            for (var cause = event.getThrowableProxy(); cause != null; cause = cause.getCause()) {
                recorded.add(cause.getMessage());
            }
        });
        stopped.forEach(context -> {
            context.getAllKeyValues().forEach(keyValue -> recorded.add(keyValue.getValue()));
            if (context.getError() != null) {
                recorded.add(context.getError().getMessage());
            }
        });
        assertThat(recorded)
                .isNotEmpty()
                .noneMatch(text -> text != null
                        && (text.contains(RECIPIENT)
                                || text.contains("ana.maria")
                                || text.contains(CODE)
                                || text.contains(API_KEY)));
    }

    private static MailMessage welcome() {
        return MailMessage.of(
                "mailprobe/welcome", RECIPIENT, Locale.ENGLISH, Locale.ENGLISH, Map.of("name", "Ana", "code", CODE));
    }

    private static MailTransport transport(String provider, Delivery delivery) {
        return new MailTransport() {
            @Override
            public String provider() {
                return provider;
            }

            @Override
            public void deliver(RenderedMail mail, MailMessage message) {
                delivery.deliver(mail, message);
            }
        };
    }

    private static List<String> keyValues(ILoggingEvent event) {
        return event.getKeyValuePairs() == null
                ? List.of()
                : event.getKeyValuePairs().stream()
                        .map(pair -> pair.key + "=" + pair.value)
                        .toList();
    }

    @FunctionalInterface
    private interface Delivery {
        void deliver(RenderedMail mail, MailMessage message);
    }
}
