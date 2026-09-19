package com.frappe.platform.infrastructure.i18n;

import static com.frappe.platform.i18n.SupportedLocales.ENGLISH;
import static com.frappe.platform.i18n.SupportedLocales.PORTUGUESE;
import static com.frappe.platform.i18n.SupportedLocales.SPANISH;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.platform.i18n.TenantLocaleDefaults;
import com.frappe.platform.i18n.TenantLocales;
import com.frappe.platform.i18n.UserLocalePreference;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpHeaders;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

class LocaleChainResolverTest {

    private static final UserLocalePreference ANONYMOUS = request -> Optional.empty();
    private static final TenantLocaleDefaults NO_TENANT = request -> Optional.empty();

    private final Logger logger = (Logger) LoggerFactory.getLogger(LocaleChainResolver.class);

    private final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        logger.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger.detachAppender(logs);
    }

    @ParameterizedTest
    @CsvSource({"es-AR, es", "pt-BR, pt", "fr, en"})
    void anonymousRequestsStartAtAcceptLanguage(String acceptLanguage, String expected) {
        // Given
        var resolver = new LocaleChainResolver(ANONYMOUS, NO_TENANT);

        // When
        var locale = resolver.resolveLocale(requestAccepting(acceptLanguage));

        // Then
        assertThat(locale).isEqualTo(Locale.forLanguageTag(expected));
    }

    @Test
    void fallsBackToEnglishWithoutHeaderAndTenantDefaults() {
        // Given
        var resolver = new LocaleChainResolver(ANONYMOUS, NO_TENANT);

        // When / Then
        assertThat(resolver.resolveLocale(new MockHttpServletRequest())).isEqualTo(ENGLISH);
    }

    @Test
    void theUsersPreferredLocaleWinsOverAcceptLanguage() {
        // Given
        var resolver = new LocaleChainResolver(request -> Optional.of(PORTUGUESE), NO_TENANT);

        // When / Then
        assertThat(resolver.resolveLocale(requestAccepting("es"))).isEqualTo(PORTUGUESE);
    }

    @Test
    void withoutHeaderTheBranchDefaultWinsOverTheBusinessDefault() {
        // Given
        var tenant = TenantLocales.of(Set.of(ENGLISH, SPANISH, PORTUGUESE), PORTUGUESE)
                .withBranchDefault(SPANISH);
        var resolver = new LocaleChainResolver(ANONYMOUS, request -> Optional.of(tenant));

        // When / Then
        assertThat(resolver.resolveLocale(new MockHttpServletRequest())).isEqualTo(SPANISH);
    }

    @Test
    void withoutHeaderAndBranchDefaultTheBusinessDefaultApplies() {
        // Given
        var tenant = TenantLocales.of(Set.of(ENGLISH, SPANISH, PORTUGUESE), PORTUGUESE);
        var resolver = new LocaleChainResolver(ANONYMOUS, request -> Optional.of(tenant));

        // When / Then
        assertThat(resolver.resolveLocale(new MockHttpServletRequest())).isEqualTo(PORTUGUESE);
    }

    @Test
    void aLanguageTheBusinessDidNotEnableFallsThroughToTheBusinessDefault() {
        // Given
        var tenant = TenantLocales.of(Set.of(SPANISH, PORTUGUESE), PORTUGUESE);
        var resolver = new LocaleChainResolver(ANONYMOUS, request -> Optional.of(tenant));

        // When / Then
        assertThat(resolver.resolveLocale(requestAccepting("en"))).isEqualTo(PORTUGUESE);
    }

    @Test
    void aPreferenceTheBusinessDidNotEnableFallsThroughToAcceptLanguage() {
        // Given
        var tenant = TenantLocales.of(Set.of(SPANISH, PORTUGUESE), PORTUGUESE);
        var resolver = new LocaleChainResolver(request -> Optional.of(ENGLISH), request -> Optional.of(tenant));

        // When / Then
        assertThat(resolver.resolveLocale(requestAccepting("es-MX"))).isEqualTo(SPANISH);
    }

    @Test
    void endsInEnglishWhenNoTenantDefaultIsAnEnabledLanguage() {
        // Given
        var tenant = TenantLocales.of(Set.of(Locale.FRENCH), Locale.FRENCH);
        var resolver = new LocaleChainResolver(ANONYMOUS, request -> Optional.of(tenant));

        // When / Then
        assertThat(resolver.resolveLocale(requestAccepting("es"))).isEqualTo(ENGLISH);
    }

    @Test
    void readsEveryAcceptLanguageHeaderLine() {
        // Given
        var request = new MockHttpServletRequest();
        request.addHeader(HttpHeaders.ACCEPT_LANGUAGE, "fr");
        request.addHeader(HttpHeaders.ACCEPT_LANGUAGE, "pt;q=0.5");
        var resolver = new LocaleChainResolver(ANONYMOUS, NO_TENANT);

        // When / Then
        assertThat(resolver.resolveLocale(request)).isEqualTo(PORTUGUESE);
    }

    @Test
    void resolvesOncePerRequest() {
        // Given
        var lookups = new AtomicInteger();
        var resolver = new LocaleChainResolver(
                request -> {
                    lookups.incrementAndGet();
                    return Optional.of(SPANISH);
                },
                NO_TENANT);
        var request = new MockHttpServletRequest();

        // When
        resolver.resolveLocale(request);
        var second = resolver.resolveLocale(request);

        // Then
        assertThat(second).isEqualTo(SPANISH);
        assertThat(lookups).hasValue(1);
    }

    @Test
    void rejectsChangingTheLocaleOfARequest() {
        // Given
        var resolver = new LocaleChainResolver(ANONYMOUS, NO_TENANT);

        // When / Then
        assertThatExceptionOfType(UnsupportedOperationException.class)
                .isThrownBy(
                        () -> resolver.setLocale(new MockHttpServletRequest(), new MockHttpServletResponse(), SPANISH))
                .withMessageContaining("UserLocalePreference");
    }

    @Test
    void aFailingUserPreferenceCountsAsNoPreferenceAndWarnsOnce() {
        // Given
        var resolver = new LocaleChainResolver(
                request -> {
                    throw new IllegalStateException("session store unavailable");
                },
                NO_TENANT);
        var request = requestAccepting("es");

        // When
        var locale = resolver.resolveLocale(request);
        resolver.resolveLocale(request);

        // Then
        assertThat(locale).isEqualTo(SPANISH);
        assertThat(logs.list).singleElement().satisfies(event -> assertWarnedAbout(event, "user_preference"));
    }

    @Test
    void aFailingTenantLookupEnablesEveryLanguageAndEndsInEnglish() {
        // Given
        TenantLocaleDefaults failing = request -> {
            throw new IllegalStateException("organization unavailable");
        };
        var resolver = new LocaleChainResolver(ANONYMOUS, failing);

        // When
        var withHeader = resolver.resolveLocale(requestAccepting("pt-BR"));
        var withoutHeader = resolver.resolveLocale(new MockHttpServletRequest());

        // Then
        assertThat(withHeader).isEqualTo(PORTUGUESE);
        assertThat(withoutHeader).isEqualTo(ENGLISH);
        assertThat(logs.list).hasSize(2).allSatisfy(event -> assertWarnedAbout(event, "tenant_defaults"));
    }

    private static void assertWarnedAbout(ILoggingEvent event, String link) {
        assertThat(event.getLevel()).isEqualTo(Level.WARN);
        assertThat(event.getKeyValuePairs()).anySatisfy(pair -> {
            assertThat(pair.key).isEqualTo("frappe.locale_link");
            assertThat(pair.value).isEqualTo(link);
        });
        assertThat(event.getThrowableProxy()).isNotNull();
    }

    private static MockHttpServletRequest requestAccepting(String acceptLanguage) {
        var request = new MockHttpServletRequest();
        request.addHeader(HttpHeaders.ACCEPT_LANGUAGE, acceptLanguage);
        return request;
    }
}
