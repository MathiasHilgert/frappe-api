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
import java.util.function.Supplier;
import org.jspecify.annotations.Nullable;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpHeaders;
import org.springframework.web.servlet.LocaleResolver;

/**
 * Resolves the one supported locale a request is answered in, walking the chain: the authenticated user's preferred
 * locale, then {@code Accept-Language}, then the branch default, then the business default, then English. With a
 * business context every link is matched only against the languages the business enabled; anonymous requests simply
 * have no preference, so they start at {@code Accept-Language}.
 *
 * <p>Localization never fails a request: a port that throws counts as "no value" for its link (a failing tenant
 * lookup therefore means every supported language is enabled) and is logged once at WARN; the chain goes on.
 *
 * <p>The result is kept as a request attribute, so the ports are asked at most once per request even though the
 * {@code Content-Language} filter and the {@code DispatcherServlet} both resolve it.
 */
final class LocaleChainResolver implements LocaleResolver {

    /** Request attribute holding the locale once resolved. */
    static final String RESOLVED_LOCALE_ATTRIBUTE = LocaleChainResolver.class.getName() + ".locale";

    private static final Logger log = LoggerFactory.getLogger(LocaleChainResolver.class);

    private static final String USER_PREFERENCE_LINK = "user_preference";
    private static final String TENANT_DEFAULTS_LINK = "tenant_defaults";

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
        var tenant = isolated(TENANT_DEFAULTS_LINK, () -> tenantDefaults.current());
        var enabled = tenant.map(settings -> LocaleMatching.all().restrictedTo(settings.enabledLanguages()))
                .orElseGet(LocaleMatching::all);
        return isolated(USER_PREFERENCE_LINK, () -> userPreference.preferredLocale())
                .flatMap(enabled::match)
                .or(() -> acceptLanguage(request).flatMap(enabled::matchAcceptLanguage))
                .or(() -> tenant.flatMap(TenantLocales::branchDefault).flatMap(enabled::match))
                .or(() -> tenant.map(TenantLocales::businessDefault).flatMap(enabled::match))
                .orElse(SupportedLocales.FALLBACK);
    }

    // Isolation boundary (see writing-code/references/errors.md): the ports run another module's code, and a failure
    // there must not fail every request, health checks included. Only this link loses its value.
    private static <T> Optional<T> isolated(String link, Supplier<Optional<T>> lookup) {
        try {
            return lookup.get();
        } catch (RuntimeException e) {
            log.atWarn()
                    .addKeyValue(LogFields.LOCALE_LINK, link)
                    .setCause(e)
                    .log("Locale lookup {} failed; resolving the locale without it", link);
            return Optional.empty();
        }
    }

    private static Optional<String> acceptLanguage(HttpServletRequest request) {
        // A header may legally be split over several lines; they form one comma-separated list.
        var lines = Collections.list(request.getHeaders(HttpHeaders.ACCEPT_LANGUAGE));
        return lines.isEmpty() ? Optional.empty() : Optional.of(String.join(",", lines));
    }
}
