package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.TenantLocaleDefaults;
import com.frappe.platform.i18n.TenantLocales;
import com.frappe.platform.i18n.UserLocalePreference;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.util.Collections;
import java.util.Locale;
import java.util.Optional;
import org.jspecify.annotations.Nullable;
import org.springframework.http.HttpHeaders;
import org.springframework.web.servlet.LocaleResolver;

/**
 * Resolves the one supported locale a request is answered in, walking the chain: the authenticated user's preferred
 * locale, then {@code Accept-Language}, then the branch default, then the business default, then English. With a
 * business context every link is matched only against the languages the business enabled; anonymous requests simply
 * have no preference, so they start at {@code Accept-Language}.
 *
 * <p>The result is kept as a request attribute, so the ports are asked at most once per request even though the
 * {@code Content-Language} filter and the {@code DispatcherServlet} both resolve it.
 */
final class LocaleChainResolver implements LocaleResolver {

    /** Request attribute holding the locale once resolved. */
    static final String RESOLVED_LOCALE_ATTRIBUTE = LocaleChainResolver.class.getName() + ".locale";

    private final UserLocalePreference userPreference;
    private final TenantLocaleDefaults tenantDefaults;

    /**
     * Creates the resolver.
     *
     * @param userPreference the authenticated user's preferred locale
     * @param tenantDefaults the enabled languages and defaults of the business the request acts for
     */
    LocaleChainResolver(UserLocalePreference userPreference, TenantLocaleDefaults tenantDefaults) {
        this.userPreference = userPreference;
        this.tenantDefaults = tenantDefaults;
    }

    @Override
    public Locale resolveLocale(HttpServletRequest request) {
        if (request.getAttribute(RESOLVED_LOCALE_ATTRIBUTE) instanceof Locale resolved) {
            return resolved;
        }
        var locale = walkChain(request);
        request.setAttribute(RESOLVED_LOCALE_ATTRIBUTE, locale);
        return locale;
    }

    @Override
    public void setLocale(HttpServletRequest request, @Nullable HttpServletResponse response, @Nullable Locale locale) {
        throw new UnsupportedOperationException("The locale of a request is resolved, never set: change the user's"
                + " preference (UserLocalePreference) or the tenant's defaults (TenantLocaleDefaults) instead");
    }

    private Locale walkChain(HttpServletRequest request) {
        var tenant = tenantDefaults.localesFor(request);
        var enabled = tenant.map(settings -> SupportedLocales.all().restrictedTo(settings.enabledLanguages()))
                .orElseGet(SupportedLocales::all);
        return userPreference
                .preferredLocale(request)
                .flatMap(enabled::match)
                .or(() -> acceptLanguage(request).flatMap(enabled::matchAcceptLanguage))
                .or(() -> tenant.flatMap(TenantLocales::branchDefault).flatMap(enabled::match))
                .or(() -> tenant.map(TenantLocales::businessDefault).flatMap(enabled::match))
                .orElse(SupportedLocales.FALLBACK);
    }

    private static Optional<String> acceptLanguage(HttpServletRequest request) {
        // A header may legally be split over several lines; they form one comma-separated list.
        var lines = Collections.list(request.getHeaders(HttpHeaders.ACCEPT_LANGUAGE));
        return lines.isEmpty() ? Optional.empty() : Optional.of(String.join(",", lines));
    }
}
