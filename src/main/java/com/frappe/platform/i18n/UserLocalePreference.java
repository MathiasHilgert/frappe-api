package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Optional;

/**
 * The locale an authenticated user chose, the first link of the locale chain. Identity implements it; until then the
 * platform treats every request as anonymous.
 *
 * <p>The platform asks at most once per HTTP request, on the request's thread, before any security filter runs and
 * with Spring's {@code RequestContextHolder} already bound: implementations read the user from the current request
 * (its session token) in their own web adapter. A failure counts as "no preference" and never fails the request.
 */
@FunctionalInterface
public interface UserLocalePreference {

    /**
     * Returns the preferred locale of the user the current request is authenticated as.
     *
     * @return the user's preferred locale, or empty for an anonymous request or a user without a preference
     */
    Optional<Locale> preferredLocale();
}
