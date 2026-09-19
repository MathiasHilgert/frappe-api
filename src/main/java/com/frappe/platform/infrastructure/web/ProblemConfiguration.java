package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.ProblemMapper;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.tracing.Tracer;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.MessageSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.Ordered;
import org.springframework.web.servlet.HandlerExceptionResolver;
import org.springframework.web.servlet.LocaleResolver;
import tools.jackson.databind.json.JsonMapper;

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
     * Every module's problem mappers; creating it fails startup when two of them overlap.
     *
     * @param mappers the mapper beans
     * @return the registry
     */
    @Bean
    ProblemMappers problemMappers(ObjectProvider<ProblemMapper<?>> mappers) {
        return new ProblemMappers(mappers.orderedStream().toList());
    }

    /**
     * The HTTP boundary filter, right inside Spring Boot's HTTP server observation filter ({@code HIGHEST_PRECEDENCE +
     * 1}), so every exception of a request is answered while its trace is current.
     *
     * @param exceptionResolver Spring MVC's exception resolvers
     * @return the filter registration
     */
    @Bean
    FilterRegistrationBean<ProblemBoundaryFilter> problemBoundaryFilter(
            @Qualifier("handlerExceptionResolver") ObjectProvider<HandlerExceptionResolver> exceptionResolver) {
        var registration = new FilterRegistrationBean<>(new ProblemBoundaryFilter(exceptionResolver::getObject));
        registration.setOrder(Ordered.HIGHEST_PRECEDENCE + 2);
        return registration;
    }

    /**
     * Answers the requests Tomcat refuses on its own with problems instead of its HTML error page.
     *
     * @param messageSource the application message source
     * @param json the application's JSON mapper
     * @return the web server customizer
     */
    @Bean
    ContainerProblemsCustomizer containerProblemsCustomizer(MessageSource messageSource, JsonMapper json) {
        return new ContainerProblemsCustomizer(new ContainerProblems(messageSource, json));
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
