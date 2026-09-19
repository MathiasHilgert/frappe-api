package com.frappe.platform.infrastructure.web;

import org.jspecify.annotations.Nullable;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.context.request.ServletWebRequest;
import org.springframework.web.context.request.WebRequest;
import org.springframework.web.servlet.mvc.method.annotation.ResponseEntityExceptionHandler;

/**
 * The API's one exception handler (it replaces Spring Boot's {@code ProblemDetailsExceptionHandler}): Spring MVC's own
 * {@link ResponseEntityExceptionHandler} decides the status and headers of every framework exception, and this advice
 * answers each with the platform's problem for that status, so no framework text reaches the client.
 */
@RestControllerAdvice
class ProblemAdvice extends ResponseEntityExceptionHandler {

    private final Problems problems;

    /**
     * Creates the advice.
     *
     * @param problems builds the problems
     */
    ProblemAdvice(Problems problems) {
        this.problems = problems;
    }

    @Override
    protected @Nullable ResponseEntity<Object> handleExceptionInternal(
            Exception ex, @Nullable Object body, HttpHeaders headers, HttpStatusCode statusCode, WebRequest request) {
        // Spring MVC hands every servlet request to its exception handlers as a ServletWebRequest.
        var servletRequest = ((ServletWebRequest) request).getRequest();
        var problem = ex instanceof MethodArgumentNotValidException invalid
                ? problems.invalidFields(invalid.getBindingResult(), servletRequest)
                : problems.forStatus(statusCode, servletRequest);
        return super.handleExceptionInternal(
                ex, problem, headers, HttpStatusCode.valueOf(problem.getStatus()), request);
    }
}
