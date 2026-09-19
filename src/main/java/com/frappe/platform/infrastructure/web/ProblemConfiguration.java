package com.frappe.platform.infrastructure.web;

import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.tracing.Tracer;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.context.MessageSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.LocaleResolver;

/**
 * Wires the problem details every failure is answered with. {@link ProblemAdvice} and {@link ProblemErrorController} are
 * found by component scanning, as Spring MVC requires for advice and controllers.
 */
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
     * Logs and counts the failures answered with the generic internal-error problem.
     *
     * @param meters the meter registry
     * @return the recorder
     */
    @Bean
    UnexpectedFailures unexpectedFailures(MeterRegistry meters) {
        return new UnexpectedFailures(meters);
    }
}
