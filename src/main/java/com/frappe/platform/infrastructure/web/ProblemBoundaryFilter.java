package com.frappe.platform.infrastructure.web;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.util.function.Supplier;
import org.springframework.web.filter.OncePerRequestFilter;
import org.springframework.web.servlet.HandlerExceptionResolver;

/**
 * The HTTP boundary: whatever the filters or the dispatcher throw ends here, is handed to Spring MVC's exception
 * resolvers (so {@link ProblemAdvice} answers it and records it once) and is never rethrown to the servlet container,
 * which would log it again (with its raw message) and render its own error page. Registered right inside the HTTP
 * server observation, so the trace id is still current.
 */
final class ProblemBoundaryFilter extends OncePerRequestFilter {

    private final Supplier<HandlerExceptionResolver> resolver;

    /**
     * Creates the boundary.
     *
     * @param resolver Spring MVC's exception resolvers (bean {@code handlerExceptionResolver}), looked up on the first
     *     failure: filters are created while the web server starts, before Spring MVC's infrastructure may be
     */
    ProblemBoundaryFilter(Supplier<HandlerExceptionResolver> resolver) {
        this.resolver = resolver;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws IOException {
        try {
            chain.doFilter(request, response);
        } catch (ServletException | IOException | RuntimeException failure) {
            // The isolation boundary of errors.md: every exception of a request, by design.
            var unwrapped = ClientFaults.unwrap(failure) instanceof Exception cause ? cause : failure;
            if (resolver.get().resolveException(request, response, null, unwrapped) == null
                    && !response.isCommitted()) {
                response.sendError(HttpServletResponse.SC_INTERNAL_SERVER_ERROR);
            }
        }
    }
}
