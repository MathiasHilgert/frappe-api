package com.frappe.platform.infrastructure.web;

import io.swagger.v3.oas.annotations.Hidden;
import jakarta.servlet.RequestDispatcher;
import jakarta.servlet.http.HttpServletRequest;
import org.jspecify.annotations.Nullable;
import org.springframework.boot.webmvc.error.ErrorAttributes;
import org.springframework.boot.webmvc.error.ErrorController;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.MediaType;
import org.springframework.http.ProblemDetail;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.context.request.ServletWebRequest;

/**
 * Answers the servlet container's error dispatches with problems, in place of Spring Boot's
 * {@code BasicErrorController}: what fails outside Spring MVC (an exception thrown by a filter, a status sent with
 * {@code sendError}) gets the same shape as everything else. A server error becomes the generic
 * internal-error problem and is recorded once; a client error (including a body the client botched) becomes the
 * platform problem for its status, unrecorded; a client that is gone gets nothing.
 *
 * <p>A framework contract, not a route: it has no {@code /v1} prefix and no posture ({@link RouteCatalog} leaves
 * {@link ErrorController}s alone); the security chain lets only error dispatches reach it.
 */
@RestController
@Hidden // springdoc: no operation of the API, like Boot's BasicErrorController
class ProblemErrorController implements ErrorController {

    private final ErrorAttributes errors;
    private final Problems problems;
    private final UnexpectedFailures unexpectedFailures;

    /**
     * Creates the controller.
     *
     * @param errors Spring Boot's view of the error dispatch (the exception, when there is one)
     * @param problems builds the problems
     * @param unexpectedFailures records failures answered with the generic problem
     */
    ProblemErrorController(ErrorAttributes errors, Problems problems, UnexpectedFailures unexpectedFailures) {
        this.errors = errors;
        this.problems = problems;
        this.unexpectedFailures = unexpectedFailures;
    }

    /**
     * Answers an error dispatch.
     *
     * @param request the error dispatch
     * @return the problem; nothing when the client is gone
     */
    @RequestMapping("${server.error.path:${error.path:/error}}")
    @Nullable
    ResponseEntity<ProblemDetail> error(HttpServletRequest request) {
        var failure = errors.getError(new ServletWebRequest(request));
        // A body cut short also surfaces as the client aborting; reading it failed first, so that decides.
        var unreadable = failure != null && ClientFaults.unreadableRequest(failure);
        if (!unreadable && failure != null && ClientFaults.clientGone(failure)) {
            return null; // nobody left to answer
        }
        var status = unreadable ? HttpStatus.BAD_REQUEST : statusOf(request);
        if (status.is5xxServerError()) {
            unexpectedFailures.record(failure, request);
        }
        var problem = problems.forStatus(status, request);
        // Set, not negotiated: the problem goes out whatever the client accepts.
        return ResponseEntity.status(problem.getStatus())
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .body(problem);
    }

    private static HttpStatusCode statusOf(HttpServletRequest request) {
        return request.getAttribute(RequestDispatcher.ERROR_STATUS_CODE) instanceof Integer code
                ? HttpStatusCode.valueOf(code)
                : HttpStatus.INTERNAL_SERVER_ERROR;
    }
}
