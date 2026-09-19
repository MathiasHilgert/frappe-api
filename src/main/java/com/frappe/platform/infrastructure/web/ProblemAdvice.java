package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.RequestRefusedException;
import jakarta.servlet.http.HttpServletRequest;
import org.jspecify.annotations.Nullable;
import org.springframework.context.NoSuchMessageException;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.core.AuthenticationException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.context.request.ServletWebRequest;
import org.springframework.web.context.request.WebRequest;
import org.springframework.web.servlet.mvc.method.annotation.ResponseEntityExceptionHandler;

/**
 * The API's one exception handler (it replaces Spring Boot's {@code ProblemDetailsExceptionHandler}): Spring MVC's own
 * {@link ResponseEntityExceptionHandler} decides the status and headers of every framework exception, and this advice
 * answers each with the platform's problem for that status, so no framework text reaches the client. Every other
 * exception is unexpected and answered with the generic internal-error problem.
 */
@RestControllerAdvice
class ProblemAdvice extends ResponseEntityExceptionHandler {

    private final Problems problems;
    private final ProblemMappers mappers;
    private final UnexpectedFailures unexpectedFailures;

    /**
     * Creates the advice.
     *
     * @param problems builds the problems
     * @param mappers the modules' problem mappers
     * @param unexpectedFailures records failures answered with the generic problem
     */
    ProblemAdvice(Problems problems, ProblemMappers mappers, UnexpectedFailures unexpectedFailures) {
        this.problems = problems;
        this.mappers = mappers;
        this.unexpectedFailures = unexpectedFailures;
    }

    /**
     * A business failure a route handed over: answered with the problem its module mapped it to. A failure without a
     * mapper, or a mapped problem whose text is missing from the catalogs, is a bug: the generic internal-error problem,
     * logged.
     *
     * @param refusal the route's refusal, carrying the failure
     * @param request the current request
     * @return the mapped problem
     */
    @ExceptionHandler(RequestRefusedException.class)
    ResponseEntity<Object> refused(RequestRefusedException refusal, HttpServletRequest request) {
        var mapped = mappers.problemOf(refusal.failure());
        if (mapped.isEmpty()) {
            return unexpected(refusal, request);
        }
        try {
            return ResponseEntity.status(mapped.get().status()).body(problems.mapped(mapped.get(), request));
        } catch (NoSuchMessageException missingText) {
            return unexpected(missingText, request);
        }
    }

    /**
     * Anything unexpected: a defect or an infrastructure fault. The client gets the generic internal-error problem,
     * never the exception; the exception is logged and counted once.
     *
     * @param failure what was thrown
     * @param request the current request
     * @return the 500 problem
     */
    @ExceptionHandler(Exception.class)
    ResponseEntity<Object> unexpected(Exception failure, HttpServletRequest request) {
        unexpectedFailures.record(failure, request);
        return ResponseEntity.internalServerError().body(problems.forStatus(HttpStatus.INTERNAL_SERVER_ERROR, request));
    }

    /**
     * An anonymous request to a route that needs a session (handed over by {@link SecurityRefusals}).
     *
     * @param refusal the security chain's refusal
     * @param request the current request
     * @return the 401 problem, asking for a bearer token
     */
    @ExceptionHandler(AuthenticationException.class)
    ResponseEntity<Object> unauthenticated(AuthenticationException refusal, HttpServletRequest request) {
        return ResponseEntity.status(HttpStatus.UNAUTHORIZED)
                .header(HttpHeaders.WWW_AUTHENTICATE, "Bearer")
                .body(problems.forStatus(HttpStatus.UNAUTHORIZED, request));
    }

    /**
     * A caller with a session on a path the security chain refuses (handed over by {@link SecurityRefusals}).
     *
     * @param refusal the security chain's refusal
     * @param request the current request
     * @return the 403 problem
     */
    @ExceptionHandler(AccessDeniedException.class)
    ResponseEntity<Object> forbidden(AccessDeniedException refusal, HttpServletRequest request) {
        return ResponseEntity.status(HttpStatus.FORBIDDEN).body(problems.forStatus(HttpStatus.FORBIDDEN, request));
    }

    @Override
    protected @Nullable ResponseEntity<Object> handleExceptionInternal(
            Exception ex, @Nullable Object body, HttpHeaders headers, HttpStatusCode statusCode, WebRequest request) {
        // Spring MVC hands every servlet request to its exception handlers as a ServletWebRequest.
        var servletRequest = ((ServletWebRequest) request).getRequest();
        if (statusCode.is5xxServerError()) {
            unexpectedFailures.record(ex, servletRequest);
        }
        var problem = ex instanceof MethodArgumentNotValidException invalid
                ? problems.invalidFields(invalid.getBindingResult(), servletRequest)
                : problems.forStatus(statusCode, servletRequest);
        return super.handleExceptionInternal(
                ex, problem, headers, HttpStatusCode.valueOf(problem.getStatus()), request);
    }
}
