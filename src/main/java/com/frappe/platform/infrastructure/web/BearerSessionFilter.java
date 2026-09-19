package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.SessionResolver;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.util.Collections;
import java.util.Optional;
import java.util.regex.Pattern;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpHeaders;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.core.context.SecurityContextHolderStrategy;
import org.springframework.security.web.context.SecurityContextRepository;
import org.springframework.web.filter.OncePerRequestFilter;

/**
 * Authenticates a request from its {@code Authorization: Bearer} header, and from nothing else: cookies and query
 * parameters are never read, so there is no CSRF surface and tokens never land in URLs or access logs. A missing,
 * malformed or unresolvable token leaves the request anonymous; the route's posture then decides (401 unless public),
 * so a stale token never blocks a public route such as sign-in.
 *
 * <p>The authenticated context is saved in the request, as Spring Security's own {@code
 * BearerTokenAuthenticationFilter} does: this filter runs once per request, and the chain's {@code
 * SecurityContextHolderFilter} reloads the context from the same repository on every later dispatch, so async
 * dispatches ({@code Callable}, {@code DeferredResult}, streaming) keep the caller.
 */
final class BearerSessionFilter extends OncePerRequestFilter {

    private static final Logger log = LoggerFactory.getLogger(BearerSessionFilter.class);

    // RFC 6750 section 2.1: the scheme is case-insensitive (RFC 9110), the token is a b64token.
    private static final Pattern BEARER = Pattern.compile("(?i)Bearer ([A-Za-z0-9\\-._~+/]+=*)");

    private final SessionResolver sessions;
    private final SecurityContextRepository repository;
    private final SecurityContextHolderStrategy contexts = SecurityContextHolder.getContextHolderStrategy();

    /**
     * Creates the filter.
     *
     * @param sessions resolves bearer tokens to sessions
     * @param repository where the chain loads the context from on every dispatch of the request
     */
    BearerSessionFilter(SessionResolver sessions, SecurityContextRepository repository) {
        this.sessions = sessions;
        this.repository = repository;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        var session = bearerToken(request).flatMap(sessions::resolve);
        if (session.isPresent()) {
            var context = contexts.createEmptyContext();
            context.setAuthentication(new SessionAuthentication(session.get()));
            contexts.setContext(context);
            repository.saveContext(context, request, response);
        } else if (request.getHeader(HttpHeaders.AUTHORIZATION) != null) {
            log.atDebug().log("Authorization header resolved to no session; the request stays anonymous");
        }
        chain.doFilter(request, response);
    }

    private static Optional<String> bearerToken(HttpServletRequest request) {
        // Two Authorization headers are ambiguous; neither is trusted.
        var headers = Collections.list(request.getHeaders(HttpHeaders.AUTHORIZATION));
        if (headers.size() != 1) {
            return Optional.empty();
        }
        var bearer = BEARER.matcher(headers.getFirst());
        return bearer.matches() ? Optional.of(bearer.group(1)) : Optional.empty();
    }
}
