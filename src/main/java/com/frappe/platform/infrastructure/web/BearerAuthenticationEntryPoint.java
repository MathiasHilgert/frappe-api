package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.springframework.http.HttpHeaders;
import org.springframework.security.core.AuthenticationException;
import org.springframework.security.web.AuthenticationEntryPoint;

/**
 * Answers an anonymous request to a route that needs a session: 401 with {@code WWW-Authenticate: Bearer} (RFC 6750)
 * and Spring's default error body.
 */
final class BearerAuthenticationEntryPoint implements AuthenticationEntryPoint {

    /** Creates the entry point. */
    BearerAuthenticationEntryPoint() {}

    @Override
    public void commence(
            HttpServletRequest request, HttpServletResponse response, AuthenticationException authException)
            throws IOException {
        response.setHeader(HttpHeaders.WWW_AUTHENTICATE, "Bearer");
        response.sendError(HttpServletResponse.SC_UNAUTHORIZED);
    }
}
