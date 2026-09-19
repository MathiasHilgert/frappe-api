package com.frappe.platform.infrastructure.web;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/** Wires the route checks and the {@code /v1} prefix. */
@Configuration(proxyBeanMethods = false)
class RouteConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    RouteConfiguration() {}

    /**
     * Serves application routes under {@code /v1}.
     *
     * @return the prefix configurer
     */
    @Bean
    WebMvcConfigurer apiPathPrefix() {
        return new ApiPathPrefix();
    }

    /**
     * The checked application routes; creating it fails startup when a route breaks the route rules.
     *
     * @param requestMappingHandlerMapping Spring MVC's annotated handler mappings
     * @return the route catalog
     */
    @Bean
    RouteCatalog routeCatalog(RequestMappingHandlerMapping requestMappingHandlerMapping) {
        return new RouteCatalog(requestMappingHandlerMapping);
    }
}
