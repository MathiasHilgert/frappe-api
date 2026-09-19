package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.ResolvedSession;
import org.springdoc.core.utils.SpringDocUtils;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Wires the OpenAPI documentation of route postures. */
@Configuration(proxyBeanMethods = false)
class OpenApiConfiguration {

    static {
        // The caller is injected, never sent: keep it out of the documented parameters. springdoc's own mechanism,
        // as its SpringDocSecurityConfiguration does for @AuthenticationPrincipal.
        SpringDocUtils.getConfig().addRequestWrapperToIgnore(ResolvedSession.class);
    }

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
