package com.frappe.platform.infrastructure.tracing;

import io.micrometer.tracing.Tracer;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.annotation.Order;

/** Registers the span handler for messaging observations that link to the creation context of their message. */
@Configuration(proxyBeanMethods = false)
class TracingConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    TracingConfiguration() {}

    /**
     * Creates the PRODUCER and CONSUMER spans of {@link LinkedMessageContext} observations. Spring Boot adds every
     * tracing handler bean to one first-matching group in bean order, so this one must come before Boot's.
     *
     * @param tracer creates the spans
     * @return the handler
     */
    @Bean
    @Order(LinkedMessageTracingHandler.ORDER)
    LinkedMessageTracingHandler linkedMessageTracingHandler(Tracer tracer) {
        return new LinkedMessageTracingHandler(tracer);
    }
}
