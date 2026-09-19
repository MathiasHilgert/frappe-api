package com.frappe.platform.infrastructure.i18n;

import static com.frappe.platform.i18n.SupportedLocales.ENGLISH;
import static com.frappe.platform.i18n.SupportedLocales.PORTUGUESE;
import static com.frappe.platform.i18n.SupportedLocales.SPANISH;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.i18n.TenantLocaleDefaults;
import com.frappe.platform.i18n.TenantLocales;
import com.frappe.platform.i18n.UserLocalePreference;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.springframework.http.HttpHeaders;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

class LocaleChainResolverTest {

    private static final UserLocalePreference ANONYMOUS = request -> Optional.empty();
    private static final TenantLocaleDefaults NO_TENANT = request -> Optional.empty();

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

    private static MockHttpServletRequest requestAccepting(String acceptLanguage) {
        var request = new MockHttpServletRequest();
        request.addHeader(HttpHeaders.ACCEPT_LANGUAGE, acceptLanguage);
        return request;
    }
}
