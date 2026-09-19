package com.frappe.platform.infrastructure.web;

import jakarta.servlet.DispatcherType;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.util.List;
import org.springframework.security.web.header.HeaderWriter;
import org.springframework.security.web.header.writers.CacheControlHeadersWriter;
import org.springframework.security.web.header.writers.HstsHeaderWriter;
import org.springframework.security.web.header.writers.XContentTypeOptionsHeaderWriter;
import org.springframework.security.web.header.writers.XXssProtectionHeaderWriter;
import org.springframework.security.web.header.writers.frameoptions.XFrameOptionsHeaderWriter;
import org.springframework.web.filter.OncePerRequestFilter;

/**
 * Spring Security's default security headers for error dispatches that had no request dispatch before (the servlet
 * container refused the request itself, TRACE for one): Spring Security's {@code HeaderWriterFilter} skips error
 * dispatches, so such an answer would go out without them. Writes Spring Security's own header writers, with its
 * defaults, and only when the headers are not there yet.
 */
final class ErrorDispatchSecurityHeaders extends OncePerRequestFilter {

    private final List<HeaderWriter> writers = List.of(
            new XContentTypeOptionsHeaderWriter(),
            new XXssProtectionHeaderWriter(),
            new CacheControlHeadersWriter(),
            new HstsHeaderWriter(),
            new XFrameOptionsHeaderWriter(XFrameOptionsHeaderWriter.XFrameOptionsMode.DENY));

    /** Creates the filter. */
    ErrorDispatchSecurityHeaders() {}

    @Override
    protected boolean shouldNotFilterErrorDispatch() {
        return false;
    }

    @Override
    protected boolean shouldNotFilter(HttpServletRequest request) {
        return request.getDispatcherType() != DispatcherType.ERROR;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        if (!response.containsHeader("X-Content-Type-Options")) {
            writers.forEach(writer -> writer.writeHeaders(request, response));
        }
        chain.doFilter(request, response);
    }
}
