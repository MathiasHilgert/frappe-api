package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletRequest;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import org.springframework.beans.factory.BeanFactoryUtils;
import org.springframework.beans.factory.ListableBeanFactory;
import org.springframework.beans.factory.SmartInitializingSingleton;
import org.springframework.boot.webmvc.actuate.endpoint.web.AdditionalHealthEndpointPathsWebMvcHandlerMapping;
import org.springframework.boot.webmvc.actuate.endpoint.web.ControllerEndpointHandlerMapping;
import org.springframework.boot.webmvc.actuate.endpoint.web.WebMvcEndpointHandlerMapping;
import org.springframework.core.annotation.AnnotationAwareOrderComparator;
import org.springframework.web.servlet.HandlerMapping;
import org.springframework.web.servlet.function.RouterFunction;
import org.springframework.web.servlet.function.support.RouterFunctionMapping;
import org.springframework.web.servlet.handler.AbstractUrlHandlerMapping;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/**
 * Keeps every handler outside the annotated routes from serving requests without a posture.
 *
 * <p>At startup it fails for any {@link RouterFunction} bean (a functional route cannot declare {@code @Access}) and
 * for any handler mapping that could serve paths or shadow a route. Besides the annotated-route mapping only these are
 * allowed: the actuator mappings (by exact type; the chain governs their paths with explicit rules), functional
 * mappings without a router function, and URL mappings without handlers (bean names, welcome page; static resources
 * are off), none but the first two ordered before the annotated routes. Mappings are read as
 * {@code DispatcherServlet} reads them. At runtime, as defense in depth, it tells whether another mapping would serve
 * a request no route matches, or would shadow a route a request matches.
 */
final class HandlerMappingGuard implements SmartInitializingSingleton {

    /**
     * Actuator mappings, exempt by exact type: they serve only below the management base path, which the security
     * chain governs with explicit rules (health public, every other endpoint closed).
     */
    // ControllerEndpointHandlerMapping is deprecated for removal in Boot 4.1 but still registered; exempt while it is.
    @SuppressWarnings("removal")
    private static final Set<Class<?>> ACTUATOR_MAPPINGS = Set.of(
            WebMvcEndpointHandlerMapping.class,
            ControllerEndpointHandlerMapping.class,
            AdditionalHealthEndpointPathsWebMvcHandlerMapping.class);

    private final ListableBeanFactory beans;
    private final RequestMappingHandlerMapping routes;
    private volatile List<HandlerMapping> others;
    private volatile List<HandlerMapping> ahead;

    /**
     * Creates the guard; the mappings are checked once every singleton exists.
     *
     * @param beans the application's beans
     * @param routes the mapping of the annotated routes, checked by the route catalog
     */
    HandlerMappingGuard(ListableBeanFactory beans, RequestMappingHandlerMapping routes) {
        this.beans = beans;
        this.routes = routes;
    }

    /**
     * Checks the handler mappings and functional routes.
     *
     * @throws InvalidRouteException when a functional route or a mapping that serves paths exists
     */
    @Override
    public void afterSingletonsInstantiated() {
        var problems = new ArrayList<String>();
        for (var name : beans.getBeanNamesForType(RouterFunction.class)) {
            problems.add("Bean '" + name + "' is a RouterFunction; functional routes cannot declare @Access, so"
                    + " declare an annotated route class instead");
        }
        var mappings = new ArrayList<HandlerMapping>();
        // The same lookup DispatcherServlet uses to collect its handler mappings.
        BeanFactoryUtils.beansOfTypeIncludingAncestors(beans, HandlerMapping.class, true, false)
                .forEach((name, mapping) -> {
                    if (mapping != routes) {
                        problemOf(name, mapping).ifPresent(problems::add);
                        mappings.add(mapping);
                    }
                });
        if (!problems.isEmpty()) {
            throw new InvalidRouteException(problems);
        }
        AnnotationAwareOrderComparator.sort(mappings);
        this.ahead = mappings.stream().filter(this::isOrderedBeforeRoutes).toList();
        this.others = List.copyOf(mappings);
    }

    /**
     * Whether a handler outside the annotated routes would serve a request.
     *
     * @param request a request no annotated route serves
     * @return {@code true} when another mapping returns a handler, fails to answer, or the mappings are not checked
     *     yet (fail closed)
     */
    boolean servesOutsideRoutes(HttpServletRequest request) {
        return anyServes(others, request);
    }

    /**
     * Whether a mapping ordered before the annotated routes would take a request a route matches, so the route's
     * posture would guard another handler.
     *
     * @param request a request an annotated route matches
     * @return {@code true} when such a mapping returns a handler, fails to answer, or the mappings are not checked yet
     *     (fail closed)
     */
    boolean shadowsRoutes(HttpServletRequest request) {
        return anyServes(ahead, request);
    }

    private static boolean anyServes(List<HandlerMapping> mappings, HttpServletRequest request) {
        if (mappings == null) {
            return true;
        }
        return ParsedRequestPath.during(request, () -> mappings.stream().anyMatch(mapping -> serves(mapping, request)));
    }

    private static boolean serves(HandlerMapping mapping, HttpServletRequest request) {
        try {
            return mapping.getHandler(request) != null;
        } catch (Exception cannotTell) {
            // HandlerMapping#getHandler declares Exception; a mapping that cannot answer may serve: fail closed.
            return true;
        }
    }

    private Optional<String> problemOf(String name, HandlerMapping mapping) {
        if (ACTUATOR_MAPPINGS.contains(mapping.getClass())) {
            return Optional.empty();
        }
        if (mapping instanceof RouterFunctionMapping functional) {
            // Spring MVC orders its own functional mapping before the annotated routes; without a router function it
            // serves nothing, and one fed later is caught per request by shadowsRoutes.
            return functional.getRouterFunction() == null
                    ? Optional.empty()
                    : Optional.of("Handler mapping '" + name + "' serves a router function; functional routes"
                            + " cannot declare @Access, so declare annotated route classes instead");
        }
        if (isOrderedBeforeRoutes(mapping)) {
            return Optional.of("Handler mapping '" + name + "' is ordered before the annotated routes and could"
                    + " shadow them; give it a lower precedence or declare route classes with @Access");
        }
        if (mapping instanceof AbstractUrlHandlerMapping urls) {
            var served = handledPaths(urls);
            return served.isEmpty()
                    ? Optional.empty()
                    : Optional.of("Handler mapping '" + name + "' serves " + served + " outside the annotated"
                            + " routes; declare them as route classes with @Access");
        }
        return Optional.of(
                "Handler mapping '" + name + "' (" + mapping.getClass().getName() + ") can serve"
                        + " requests outside the annotated routes; declare them as route classes with @Access");
    }

    private boolean isOrderedBeforeRoutes(HandlerMapping mapping) {
        // Equal orders keep registration order in DispatcherServlet, so a tie may run first as well.
        return AnnotationAwareOrderComparator.INSTANCE.compare(mapping, routes) <= 0;
    }

    private static List<String> handledPaths(AbstractUrlHandlerMapping urls) {
        var paths = new ArrayList<>(urls.getHandlerMap().keySet());
        if (urls.getRootHandler() != null) {
            paths.add("/");
        }
        if (urls.getDefaultHandler() != null) {
            paths.add("/**");
        }
        return paths;
    }
}
