package com.frappe.platform.infrastructure.web;

/** Which annotated handler, if any, Spring MVC will dispatch a request to. */
sealed interface RouteMatch {

    /**
     * An application route serves the request; its posture decides.
     *
     * @param route the serving route
     */
    record ApplicationRoute(Route route) implements RouteMatch {}

    /**
     * A framework controller serves the request, or several mappings match equally well (Spring MVC fails the request
     * as ambiguous). Refused unless the security chain permits the path explicitly: fail closed.
     */
    record OtherHandler() implements RouteMatch {}

    /**
     * No annotated handler matches, so no controller code can run; Spring MVC answers 404, or 405 with {@code Allow}
     * when the path exists for other methods.
     */
    record NoHandler() implements RouteMatch {}
}
