package com.frappe.platform.infrastructure.i18n;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.util.Arrays;
import org.springframework.http.HttpHeaders;
import org.springframework.web.filter.OncePerRequestFilter;
import org.springframework.web.servlet.LocaleResolver;

/**
 * Announces the language of every response: {@code Content-Language} carries the resolved locale, and
 * {@code Vary: Accept-Language} tells caches that the same URL is answered differently per requested language, so a
 * cache never serves one language to a client asking for another.
 *
 * <p>Headers are written before the rest of the chain runs, while the response cannot be committed yet. Error
 * dispatches are covered too: an error the servlet container raised itself (TRACE, for one) never had a request
 * dispatch through this filter.
 */
final class ContentLanguageFilter extends OncePerRequestFilter {

    private final LocaleResolver localeResolver;

    /**
     * Creates the filter.
     *
     * @param localeResolver resolves the locale the request is answered in
     */
    ContentLanguageFilter(LocaleResolver localeResolver) {
        this.localeResolver = localeResolver;
    }

    @Override
    protected boolean shouldNotFilterErrorDispatch() {
        return false;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        response.setHeader(
                HttpHeaders.CONTENT_LANGUAGE,
                localeResolver.resolveLocale(request).toLanguageTag());
        if (!variesByAcceptLanguage(response)) {
            response.addHeader(HttpHeaders.VARY, HttpHeaders.ACCEPT_LANGUAGE);
        }
        chain.doFilter(request, response);
    }

    private static boolean variesByAcceptLanguage(HttpServletResponse response) {
        return response.getHeaders(HttpHeaders.VARY).stream()
                .flatMap(line -> Arrays.stream(line.split(",")))
                .map(String::trim)
                .anyMatch(HttpHeaders.ACCEPT_LANGUAGE::equalsIgnoreCase);
    }
}
