package com.frappe.platform.infrastructure.web;

import java.util.List;
import org.springframework.beans.factory.ListableBeanFactory;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.method.support.HandlerMethodArgumentResolver;
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
     * Injects the caller into route methods as a plain {@code ResolvedSession} parameter.
     *
     * @return the configurer registering the argument resolver
     */
    @Bean
    WebMvcConfigurer resolvedSessionArguments() {
        return new WebMvcConfigurer() {
            @Override
            public void addArgumentResolvers(List<HandlerMethodArgumentResolver> resolvers) {
                resolvers.add(new ResolvedSessionArgumentResolver());
            }
        };
    }

    /**
     * Fails startup for functional routes and handler mappings that serve paths outside the annotated routes, and
     * tells the security chain at runtime whether such a handler would serve a request.
     *
     * @param beans the application's beans
     * @param requestMappingHandlerMapping Spring MVC's annotated handler mappings
     * @return the guard
     */
    @Bean
    HandlerMappingGuard handlerMappingGuard(
            ListableBeanFactory beans, RequestMappingHandlerMapping requestMappingHandlerMapping) {
        return new HandlerMappingGuard(beans, requestMappingHandlerMapping);
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
