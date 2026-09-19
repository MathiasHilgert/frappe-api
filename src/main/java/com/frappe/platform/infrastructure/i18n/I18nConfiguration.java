package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.TenantLocaleDefaults;
import com.frappe.platform.i18n.UserLocalePreference;
import java.util.Optional;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.Ordered;
import org.springframework.web.servlet.DispatcherServlet;
import org.springframework.web.servlet.LocaleResolver;

/**
 * Wires locale resolution: the chain resolver replaces Boot's {@code Accept-Language}-only resolver, and the
 * {@code Content-Language} filter announces its result on every response.
 */
@Configuration(proxyBeanMethods = false)
class I18nConfiguration {

    /**
     * Runs inside the HTTP server observation (Boot orders it at {@code HIGHEST_PRECEDENCE + 1}), so lookups of the
     * ports are traced with the request, and before security filters, so their error responses carry the headers too.
     */
    static final int CONTENT_LANGUAGE_FILTER_ORDER = Ordered.HIGHEST_PRECEDENCE + 10;

    /** Creates the configuration; instantiated by Spring. */
    I18nConfiguration() {}

    /**
     * The locale chain, registered under the name the {@code DispatcherServlet} looks up (Boot's own resolver backs
     * off). Until identity and organization provide the ports, every request is anonymous and has no business context.
     *
     * @param userPreference identity's port, when present
     * @param tenantDefaults organization's port, when present
     * @return the locale resolver
     */
    @Bean(DispatcherServlet.LOCALE_RESOLVER_BEAN_NAME)
    LocaleResolver localeResolver(
            ObjectProvider<UserLocalePreference> userPreference, ObjectProvider<TenantLocaleDefaults> tenantDefaults) {
        return new LocaleChainResolver(
                userPreference.getIfAvailable(() -> request -> Optional.empty()),
                tenantDefaults.getIfAvailable(() -> request -> Optional.empty()));
    }

    /**
     * Registers the {@code Content-Language} / {@code Vary} filter for every request.
     *
     * @param localeResolver the locale chain
     * @return the filter registration
     */
    @Bean
    FilterRegistrationBean<ContentLanguageFilter> contentLanguageFilter(LocaleResolver localeResolver) {
        var registration = new FilterRegistrationBean<>(new ContentLanguageFilter(localeResolver));
        registration.setOrder(CONTENT_LANGUAGE_FILTER_ORDER);
        return registration;
    }
}
