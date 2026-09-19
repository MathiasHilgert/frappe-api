package com.frappe.platform.infrastructure.web;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Wires the OpenAPI documentation of route postures. */
@Configuration(proxyBeanMethods = false)
class OpenApiConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    OpenApiConfiguration() {}

    /**
     * Documents route postures and the bearer scheme in the generated spec.
     *
     * @return the customizer, registered with springdoc as operation and document customizer
     */
    @Bean
    PostureDocumentation postureDocumentation() {
        return new PostureDocumentation();
    }
}
