package com.frappe.platform.i18n;

import java.util.Optional;

/**
 * The locale settings of the business (and branch) the current request acts for: the languages the business enabled
 * and its defaults. Organization implements it; until then no request has a business context and every supported
 * language is enabled.
 *
 * <p>The platform asks at most once per HTTP request, on the request's thread, before any security filter runs and
 * with Spring's {@code RequestContextHolder} already bound: implementations derive the business from the current
 * request (a staff member's session, or the branch an anonymous diner is browsing) in their own web adapter. A failure
 * counts as "no business context" (every language enabled) and never fails the request.
 */
@FunctionalInterface
public interface TenantLocaleDefaults {

    /**
     * Returns the locale settings of the business the current request acts for.
     *
     * @return the business's locale settings, or empty without a business context
     */
    Optional<TenantLocales> current();
}
