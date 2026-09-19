package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicBoolean;
import org.apache.catalina.connector.Request;
import org.apache.catalina.connector.Response;
import org.apache.catalina.valves.ErrorReportValve;
import org.apache.coyote.ActionCode;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;

/**
 * Tomcat's error report, as a problem: replaces {@link ErrorReportValve}'s HTML page for the errors Tomcat answers on
 * its own. It follows the valve's rules (only an unreported error with nothing written yet, only while the connection
 * still carries an answer) and writes the static problem of the status, never the cause.
 */
final class ProblemErrorReportValve extends ErrorReportValve {

    private final ContainerProblems problems;

    /**
     * Creates the valve.
     *
     * @param problems renders the problem bodies
     */
    ProblemErrorReportValve(ContainerProblems problems) {
        this.problems = problems;
        setShowReport(false);
        setShowServerInfo(false);
    }

    @Override
    protected void report(Request request, Response response, Throwable throwable) {
        var status = response.getStatus();
        if (status < HttpServletResponse.SC_BAD_REQUEST
                || response.getContentWritten() > 0
                || !response.setErrorReported()) {
            return;
        }
        var ioAllowed = new AtomicBoolean(false);
        response.getCoyoteResponse().action(ActionCode.IS_IO_ALLOWED, ioAllowed);
        if (!ioAllowed.get()) {
            return;
        }
        try {
            response.setContentType(MediaType.APPLICATION_PROBLEM_JSON_VALUE);
            response.setCharacterEncoding(StandardCharsets.UTF_8.name());
            response.setHeader(HttpHeaders.CONTENT_LANGUAGE, "en");
            response.setHeader(HttpHeaders.VARY, HttpHeaders.ACCEPT_LANGUAGE);
            var writer = response.getReporter();
            if (writer != null) {
                writer.write(problems.bodyFor(status));
                response.finishResponse();
            }
        } catch (IOException | IllegalStateException connectionGone) {
            // As Tomcat's own valve: the client no longer reads the answer.
        }
    }
}
