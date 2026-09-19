package com.frappe.platform.i18n;

import jakarta.servlet.http.HttpServletRequest;
import java.util.Locale;
import java.util.Optional;

/**
 * The locale an authenticated user chose, the first link of the locale chain. Identity implements it; until then the
 * platform treats every request as anonymous.
 *
 * <p>Implementations are singletons that read the user from the request they are given (its session), so the chain
 * does not depend on the order of servlet filters. They are called at most once per request.
 */
@FunctionalInterface
public interface UserLocalePreference {

    /**
     * Returns the preferred locale of the user the request is authenticated as.
     *
     * @param request the current request
     * @return the user's preferred locale, or empty for an anonymous request or a user without a preference
     */
    Optional<Locale> preferredLocale(HttpServletRequest request);
}
