package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.i18n.SupportedLocales;
import java.util.Locale;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.mock.web.MockFilterChain;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.web.servlet.i18n.FixedLocaleResolver;

class ContentLanguageFilterTest {

    private final ContentLanguageFilter filter =
            new ContentLanguageFilter(new FixedLocaleResolver(SupportedLocales.SPANISH));

    @Test
    void announcesTheResolvedLanguageAndThatTheResponseVariesByAcceptLanguage() throws Exception {
        // Given
        var response = new MockHttpServletResponse();

        // When
        filter.doFilter(new MockHttpServletRequest(), response, new MockFilterChain());

        // Then
        assertThat(response.getHeader(HttpHeaders.CONTENT_LANGUAGE)).isEqualTo("es");
        assertThat(response.getHeaders(HttpHeaders.VARY)).containsExactly(HttpHeaders.ACCEPT_LANGUAGE);
    }

    @Test
    void keepsOtherVaryValuesAndNeverRepeatsAcceptLanguage() throws Exception {
        // Given
        var response = new MockHttpServletResponse();
        response.addHeader(HttpHeaders.VARY, "Origin, accept-language");

        // When
        filter.doFilter(new MockHttpServletRequest(), response, new MockFilterChain());

        // Then
        assertThat(response.getHeaders(HttpHeaders.VARY)).containsExactly("Origin, accept-language");
    }

    @Test
    void writesTheLanguageAsABcp47Tag() throws Exception {
        // Given
        var regional = new ContentLanguageFilter(new FixedLocaleResolver(Locale.forLanguageTag("pt-BR")));
        var response = new MockHttpServletResponse();

        // When
        regional.doFilter(new MockHttpServletRequest(), response, new MockFilterChain());

        // Then
        assertThat(response.getHeader(HttpHeaders.CONTENT_LANGUAGE)).isEqualTo("pt-BR");
    }
}
