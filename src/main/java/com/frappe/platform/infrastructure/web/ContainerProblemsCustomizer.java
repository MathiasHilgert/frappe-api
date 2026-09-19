package com.frappe.platform.infrastructure.web;

import org.apache.catalina.core.StandardHost;
import org.apache.catalina.valves.ErrorReportValve;
import org.springframework.boot.tomcat.servlet.TomcatServletWebServerFactory;
import org.springframework.boot.web.server.WebServerFactoryCustomizer;
import org.springframework.core.Ordered;

/**
 * Puts {@link ProblemErrorReportValve} in place of every {@link ErrorReportValve} of the host, including the one Spring
 * Boot adds for {@code server.error.include-stacktrace=never}. Ordered last, so its context customizer runs after
 * Boot's.
 */
final class ContainerProblemsCustomizer implements WebServerFactoryCustomizer<TomcatServletWebServerFactory>, Ordered {

    private final ContainerProblems problems;

    /**
     * Creates the customizer.
     *
     * @param problems renders the problem bodies
     */
    ContainerProblemsCustomizer(ContainerProblems problems) {
        this.problems = problems;
    }

    @Override
    public void customize(TomcatServletWebServerFactory factory) {
        factory.addContextCustomizers(context -> {
            var host = (StandardHost) context.getParent();
            var pipeline = host.getPipeline();
            for (var valve : pipeline.getValves()) {
                if (valve instanceof ErrorReportValve) {
                    pipeline.removeValve(valve);
                }
            }
            // The host adds its default report valve on start unless one of this class is present.
            host.setErrorReportValveClass(ProblemErrorReportValve.class.getName());
            pipeline.addValve(new ProblemErrorReportValve(problems));
        });
    }

    @Override
    public int getOrder() {
        return Ordered.LOWEST_PRECEDENCE;
    }
}
