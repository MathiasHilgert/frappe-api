package com.frappe.platform.i18n;

import jakarta.servlet.http.HttpServletRequest;
import java.util.Optional;

/**
 * The locale settings of the business (and branch) a request acts for: the languages the business enabled and its
 * defaults. Organization implements it; until then no request has a business context and every supported language is
 * enabled.
 *
 * <p>Implementations are singletons that derive the business from the request they are given (the session of a staff
 * member, or the branch an anonymous diner is browsing). They are called at most once per request.
 */
@FunctionalInterface
public interface TenantLocaleDefaults {

    /**
     * Returns the locale settings of the business the request acts for.
     *
     * @param request the current request
     * @return the business's locale settings, or empty without a business context
     */
    Optional<TenantLocales> localesFor(HttpServletRequest request);
}
