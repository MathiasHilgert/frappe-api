package com.frappe.platform.infrastructure.web;

import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.core.AuthenticationException;
import org.springframework.security.web.AuthenticationEntryPoint;
import org.springframework.security.web.access.AccessDeniedHandler;
import org.springframework.web.servlet.HandlerExceptionResolver;

/**
 * Answers the security chain's refusals like every other failure: it hands them to Spring MVC's exception resolvers
 * (the delegation Spring Security documents for {@code @ControllerAdvice} handling), so {@link ProblemAdvice} answers an
 * anonymous request to a protected route with the 401 problem and {@code WWW-Authenticate: Bearer} (RFC 6750), and a
 * refused caller with a session with the 403 problem. Should no resolver answer, the plain status is sent.
 */
final class SecurityRefusals implements AuthenticationEntryPoint, AccessDeniedHandler {

    private final HandlerExceptionResolver resolver;

    /**
     * Creates the handler.
     *
     * @param resolver Spring MVC's exception resolvers (bean {@code handlerExceptionResolver})
     */
    SecurityRefusals(HandlerExceptionResolver resolver) {
        this.resolver = resolver;
    }

    @Override
    public void commence(
            HttpServletRequest request, HttpServletResponse response, AuthenticationException authException)
            throws IOException {
        answer(request, response, authException, HttpServletResponse.SC_UNAUTHORIZED);
    }

    @Override
    public void handle(
            HttpServletRequest request, HttpServletResponse response, AccessDeniedException accessDeniedException)
            throws IOException, ServletException {
        answer(request, response, accessDeniedException, HttpServletResponse.SC_FORBIDDEN);
    }

    private void answer(HttpServletRequest request, HttpServletResponse response, Exception refusal, int status)
            throws IOException {
        if (resolver.resolveException(request, response, null, refusal) == null) {
            response.sendError(status);
        }
    }
}
