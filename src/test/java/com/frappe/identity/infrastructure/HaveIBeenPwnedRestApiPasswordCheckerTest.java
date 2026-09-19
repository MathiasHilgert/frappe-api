package com.frappe.identity.infrastructure;

import static com.github.tomakehurst.wiremock.client.WireMock.aResponse;
import static com.github.tomakehurst.wiremock.client.WireMock.get;
import static com.github.tomakehurst.wiremock.client.WireMock.getRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.urlEqualTo;
import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.CapturedLogs;
import com.frappe.identity.domain.BreachStatus;
import com.frappe.identity.domain.Password;
import com.github.tomakehurst.wiremock.junit5.WireMockExtension;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Duration;
import java.util.HexFormat;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.RegisterExtension;
import org.slf4j.LoggerFactory;

/** Contract tests of the Pwned Passwords range API adapter against a WireMock stub; never calls the real API. */
class HaveIBeenPwnedRestApiPasswordCheckerTest {

    private static final String RAW = "correct horse battery staple";

    @RegisterExtension
    static WireMockExtension pwnedPasswords = WireMockExtension.newInstance().build();

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final Logger root = (Logger) LoggerFactory.getLogger(Logger.ROOT_LOGGER_NAME);

    private ListAppender<ILoggingEvent> logs;

    private HaveIBeenPwnedRestApiPasswordChecker checker;

    private String prefix;

    private String suffix;

    @BeforeEach
    void setUp() throws NoSuchAlgorithmException {
        var properties = new IdentityProperties.BreachedPasswordsProperties(
                URI.create(pwnedPasswords.baseUrl()), Duration.ofMillis(300));
        checker = new HaveIBeenPwnedRestApiPasswordChecker(properties, observations);
        var sha1 = HexFormat.of()
                .withUpperCase()
                .formatHex(MessageDigest.getInstance("SHA-1").digest(RAW.getBytes(StandardCharsets.UTF_8)));
        prefix = sha1.substring(0, 5);
        suffix = sha1.substring(5);
        logs = CapturedLogs.fromCurrentThread();
        root.addAppender(logs);
    }

    @AfterEach
    void detachLogs() {
        root.detachAppender(logs);
    }

    @Test
    void aPasswordWhoseSuffixTheRangeListsIsBreachedAndOnlyThePrefixIsSent() {
        // Given
        stubRange("0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n" + suffix
                + ":3861493\r\nFFFFF45C4D1DEF81644B54AB7F969B88D65:2");

        // When
        var status = checker.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.BREACHED);
        pwnedPasswords.verify(getRequestedFor(urlEqualTo("/range/" + prefix)));
        assertThat(pwnedPasswords.getAllServeEvents()).singleElement().satisfies(served -> {
            assertThat(served.getRequest().getUrl()).doesNotContain(suffix);
            assertThat(served.getRequest().getHeaders().all().toString()).doesNotContain(suffix);
            assertThat(served.getRequest().getBodyAsString()).isEmpty();
        });
    }

    @Test
    void theRequestAsksForPaddedResponses() {
        // Given
        stubRange("0018A45C4D1DEF81644B54AB7F969B88D65:1");

        // When
        checker.check(password());

        // Then
        pwnedPasswords.verify(getRequestedFor(urlEqualTo("/range/" + prefix))
                .withHeader("Add-Padding", com.github.tomakehurst.wiremock.client.WireMock.equalTo("true")));
    }

    @Test
    void aPasswordWhoseSuffixTheRangeDoesNotListIsNotFound() {
        // Given
        stubRange("0018A45C4D1DEF81644B54AB7F969B88D65:1");

        // When
        var status = checker.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.NOT_FOUND);
    }

    @Test
    void aPaddingEntryWithCountZeroIsNotABreach() {
        // Given
        stubRange(suffix + ":0");

        // When
        var status = checker.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.NOT_FOUND);
    }

    @Test
    void aServerErrorIsUnknownRecordedOnTheClientObservationAndNotLogged() {
        // Given
        pwnedPasswords.stubFor(get("/range/" + prefix).willReturn(aResponse().withStatus(503)));

        // When
        var status = checker.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.UNKNOWN);
        assertTheClientObservationRecordedAnError();
        assertNothingLoggedAtWarnOrAbove();
    }

    @Test
    void aTimeoutIsUnknownRecordedOnTheClientObservationAndNotLogged() {
        // Given
        pwnedPasswords.stubFor(get("/range/" + prefix)
                .willReturn(aResponse().withStatus(200).withBody("").withFixedDelay(2_000)));

        // When
        var status = checker.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.UNKNOWN);
        assertTheClientObservationRecordedAnError();
        assertNothingLoggedAtWarnOrAbove();
    }

    @Test
    void anUnreachableServiceIsUnknown() {
        // Given
        var properties = new IdentityProperties.BreachedPasswordsProperties(
                URI.create("http://127.0.0.1:1"), Duration.ofMillis(300));
        var unreachable = new HaveIBeenPwnedRestApiPasswordChecker(properties, observations);

        // When
        var status = unreachable.check(password());

        // Then
        assertThat(status).isEqualTo(BreachStatus.UNKNOWN);
        assertNothingLoggedAtWarnOrAbove();
    }

    private void stubRange(String body) {
        pwnedPasswords.stubFor(
                get("/range/" + prefix).willReturn(aResponse().withStatus(200).withBody(body)));
    }

    private void assertTheClientObservationRecordedAnError() {
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("http.client.requests")
                .hasError()
                .hasBeenStopped();
    }

    private void assertNothingLoggedAtWarnOrAbove() {
        assertThat(logs.list)
                .filteredOn(event -> event.getLevel().isGreaterOrEqual(Level.WARN))
                .isEmpty();
    }

    private static Password password() {
        return Password.of(RAW).orElseThrow(rejected -> new AssertionError("rejected: " + rejected));
    }
}
