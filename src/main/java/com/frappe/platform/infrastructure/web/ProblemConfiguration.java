package com.frappe.platform.infrastructure.web;

import io.micrometer.tracing.Tracer;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.context.MessageSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.LocaleResolver;

/** Wires the problem details every failure is answered with. */
@Configuration(proxyBeanMethods = false)
class ProblemConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    ProblemConfiguration() {}

    /**
     * Builds the problems, localized with the application message source in the request's locale.
     *
     * @param messageSource the application message source
     * @param localeResolver the locale chain
     * @param tracer the tracer, absent when tracing is off
     * @return the problem factory
     */
    @Bean
    Problems problems(MessageSource messageSource, LocaleResolver localeResolver, ObjectProvider<Tracer> tracer) {
        return new Problems(messageSource, localeResolver, tracer.getIfAvailable(() -> Tracer.NOOP));
    }

    /**
     * The one exception handler of the API.
     *
     * @param problems builds the problems
     * @return the advice
     */
    @Bean
    ProblemAdvice problemAdvice(Problems problems) {
        return new ProblemAdvice(problems);
    }
}
