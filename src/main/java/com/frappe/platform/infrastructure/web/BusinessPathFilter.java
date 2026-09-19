package com.frappe.platform.infrastructure.web;

import com.frappe.platform.TenantScope;
import com.frappe.platform.web.BusinessMembership;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.util.Optional;
import java.util.UUID;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.filter.OncePerRequestFilter;

/**
 * Lets a request into the business its path names, {@code /v1/businesses/{businessId}/…}, and binds that business as
 * the request's tenant. It runs after the route's posture was enforced, once per request, and asks again on every
 * request (nothing is cached across requests).
 *
 * <ul>
 *   <li>A segment that is not a UUID is not found.
 *   <li>On an {@code AUTHENTICATED} route the caller enters when its session is bound to that business, or when it is a
 *       person the {@link BusinessMembership} port calls a member. Everyone else is not found, exactly as for a
 *       business that does not exist: a stranger never learns that a business exists (404 before 403), and a staff or
 *       terminal session never crosses into another business.
 *   <li>A {@code PUBLIC} route under the path runs for anyone, with the business bound.
 * </ul>
 *
 * <p>A refusal is answered by {@link SecurityRefusals} as the {@code not-found} problem before any controller code
 * runs. A failing membership port is not a refusal: it propagates and answers the generic 500 (fail closed).
 */
final class BusinessPathFilter extends OncePerRequestFilter {

    private final RouteCatalog routes;
    private final BusinessMembership memberships;
    private final TenantScope tenants;

    /**
     * Creates the filter.
     *
     * @param routes tells which route serves a request
     * @param memberships tells whether a person belongs to a business
     * @param tenants binds the business as the request's tenant
     */
    BusinessPathFilter(RouteCatalog routes, BusinessMembership memberships, TenantScope tenants) {
        this.routes = routes;
        this.memberships = memberships;
        this.tenants = tenants;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        if (!(routes.match(request) instanceof RouteMatch.ApplicationRoute(var route))) {
            chain.doFilter(request, response);
            return;
        }
        var segment = BusinessPath.segmentOf(route, request);
        if (segment.isEmpty()) {
            chain.doFilter(request, response);
            return;
        }
        var businessId = BusinessPath.businessIdOf(segment.get()).orElseThrow(() -> notFound(request));
        if (route.posture() == Posture.AUTHENTICATED && !callerMayEnter(businessId)) {
            throw notFound(request);
        }
        continueAs(businessId, request, response, chain);
    }

    private boolean callerMayEnter(UUID businessId) {
        var caller = caller();
        if (caller.isEmpty()) {
            return false;
        }
        var session = caller.get();
        // A bound session enters its own business only. ResolvedSession guarantees that a PERSON is never bound; the
        // kind check keeps that true here even if the invariant ever changed.
        if (session.kind() != SessionKind.PERSON && session.businessId().isPresent()) {
            return session.businessId().get().equals(businessId);
        }
        return session.kind() == SessionKind.PERSON && memberships.isMember(session.principalId(), businessId);
    }

    private static Optional<ResolvedSession> caller() {
        return SecurityContextHolder.getContext().getAuthentication() instanceof SessionAuthentication authentication
                ? Optional.of(authentication.getPrincipal())
                : Optional.empty();
    }

    private void continueAs(
            UUID businessId, HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        try {
            tenants.runAs(businessId, () -> {
                try {
                    chain.doFilter(request, response);
                } catch (IOException | ServletException failure) {
                    throw new ChainFailure(failure);
                }
            });
        } catch (ChainFailure failure) {
            if (failure.getCause() instanceof IOException io) {
                throw io;
            }
            throw (ServletException) failure.getCause();
        }
    }

    private static AccessDeniedException notFound(HttpServletRequest request) {
        // Answered by SecurityRefusals as not found, for callers with and without a session alike.
        request.setAttribute(RouteAuthorizationManager.NOT_SERVED, Boolean.TRUE);
        return new AccessDeniedException("The caller may not enter the business of the path");
    }

    /** Carries a checked failure of the rest of the chain out of the tenant scope, to be rethrown unchanged. */
    private static final class ChainFailure extends RuntimeException {

        private static final long serialVersionUID = 1L;

        ChainFailure(Exception cause) {
            super(cause);
        }
    }
}
