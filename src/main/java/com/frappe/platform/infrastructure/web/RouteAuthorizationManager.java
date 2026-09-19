package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Posture;
import java.util.Map;
import java.util.function.Supplier;
import java.util.stream.Collectors;
import org.springframework.security.authorization.AuthenticatedAuthorizationManager;
import org.springframework.security.authorization.AuthorizationDecision;
import org.springframework.security.authorization.AuthorizationManager;
import org.springframework.security.authorization.AuthorizationResult;
import org.springframework.security.authorization.SingleResultAuthorizationManager;
import org.springframework.security.core.Authentication;
import org.springframework.security.web.access.intercept.RequestAuthorizationContext;

/**
 * Decides every request by the posture of the route that serves it, with one authorization manager per route built from
 * its posture: this is authentication enforcement only (is there a caller?), never permissions, which the use case
 * decides. A request no annotated handler serves passes through, so Spring MVC answers 404 or 405 (route shapes are
 * public in the OpenAPI spec anyway), but only when no other handler mapping would serve it. A framework controller or
 * an ambiguous match is refused unless the chain permits its path explicitly: 401 for an anonymous caller, 403 for a
 * caller with a session.
 */
final class RouteAuthorizationManager implements AuthorizationManager<RequestAuthorizationContext> {

    private static final AuthorizationDecision DENIED = new AuthorizationDecision(false);
    private static final AuthorizationDecision GRANTED = new AuthorizationDecision(true);

    private final RouteCatalog routes;
    private final HandlerMappingGuard otherHandlers;
    private final Map<Route, AuthorizationManager<RequestAuthorizationContext>> managers;

    /**
     * Builds the authorization manager of every route.
     *
     * @param routes the checked routes
     * @param otherHandlers tells whether a handler outside the routes would serve a request
     */
    RouteAuthorizationManager(RouteCatalog routes, HandlerMappingGuard otherHandlers) {
        this.routes = routes;
        this.otherHandlers = otherHandlers;
        this.managers = routes.routes().stream()
                .collect(Collectors.toUnmodifiableMap(route -> route, route -> managerFor(route.posture())));
    }

    @Override
    public AuthorizationResult authorize(
            Supplier<? extends Authentication> authentication, RequestAuthorizationContext context) {
        return switch (routes.match(context.getRequest())) {
            case RouteMatch.ApplicationRoute(var route) -> {
                var decision = managers.get(route).authorize(authentication, context);
                yield decision == null ? DENIED : decision;
            }
            case RouteMatch.OtherHandler() -> DENIED;
            // No route matches: let Spring MVC answer 404 or 405, unless another handler mapping would serve it.
            case RouteMatch.NoHandler() -> otherHandlers.servesOutsideRoutes(context.getRequest()) ? DENIED : GRANTED;
        };
    }

    private static AuthorizationManager<RequestAuthorizationContext> managerFor(Posture posture) {
        return switch (posture) {
            case PUBLIC -> SingleResultAuthorizationManager.permitAll();
            case AUTHENTICATED -> AuthenticatedAuthorizationManager.authenticated();
        };
    }
}
