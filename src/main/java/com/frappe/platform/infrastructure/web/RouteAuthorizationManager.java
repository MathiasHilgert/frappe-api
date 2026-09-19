package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.PermissionEvaluator;
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
 * Decides every request by the posture of the route that serves it, with one authorization manager per route built
 * from its posture. A request no route serves is refused. Refusing an anonymous caller answers 401, refusing a
 * caller with a session 403 (Spring Security's exception translation).
 */
final class RouteAuthorizationManager implements AuthorizationManager<RequestAuthorizationContext> {

    private static final AuthorizationDecision DENIED = new AuthorizationDecision(false);

    private final RouteCatalog routes;
    private final Map<Route, AuthorizationManager<RequestAuthorizationContext>> managers;

    /**
     * Builds the authorization manager of every route.
     *
     * @param routes the checked routes
     * @param permissions decides {@link Posture#PERMISSION} routes
     */
    RouteAuthorizationManager(RouteCatalog routes, PermissionEvaluator permissions) {
        this.routes = routes;
        this.managers = routes.routes().stream()
                .collect(Collectors.toUnmodifiableMap(route -> route, route -> managerFor(route, permissions)));
    }

    @Override
    public AuthorizationResult authorize(
            Supplier<? extends Authentication> authentication, RequestAuthorizationContext context) {
        return routes.routeFor(context.getRequest())
                .map(managers::get)
                .map(manager -> manager.authorize(authentication, context))
                .orElse(DENIED);
    }

    private static AuthorizationManager<RequestAuthorizationContext> managerFor(
            Route route, PermissionEvaluator permissions) {
        return switch (route.posture()) {
            case PUBLIC -> SingleResultAuthorizationManager.permitAll();
            case AUTHENTICATED -> AuthenticatedAuthorizationManager.authenticated();
            case PERMISSION ->
                (authentication, context) ->
                        new AuthorizationDecision(authentication.get() instanceof SessionAuthentication caller
                                && permissions.isPermitted(caller.getPrincipal(), route.permission()));
            case SYSTEM -> SingleResultAuthorizationManager.denyAll();
        };
    }
}
