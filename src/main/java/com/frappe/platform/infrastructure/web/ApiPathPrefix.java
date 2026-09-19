package com.frappe.platform.infrastructure.web;

import org.springframework.web.servlet.config.annotation.PathMatchConfigurer;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

/** Serves every application route under {@code /v1}; framework controllers (errors, OpenAPI) keep their paths. */
final class ApiPathPrefix implements WebMvcConfigurer {

    /** The prefix of every application route. */
    static final String PREFIX = "/v1";

    /** Creates the prefix configurer. */
    ApiPathPrefix() {}

    @Override
    public void configurePathMatch(PathMatchConfigurer configurer) {
        configurer.addPathPrefix(PREFIX, RouteCatalog.APPLICATION_ROUTES);
    }
}
