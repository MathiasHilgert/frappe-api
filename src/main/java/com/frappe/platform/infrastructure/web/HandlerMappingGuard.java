package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletRequest;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import org.springframework.beans.factory.ListableBeanFactory;
import org.springframework.beans.factory.SmartInitializingSingleton;
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
 * for any handler mapping that could serve paths: only the annotated-route mapping, the functional mapping (empty
 * without {@code RouterFunction} beans), URL mappings without handlers (bean names, welcome page; static resources are
 * off) and the actuator mappings (governed by explicit security rules) are allowed. At runtime, as defense in depth, it
 * tells whether any of those other mappings returns a handler for a request no route serves.
 */
final class HandlerMappingGuard implements SmartInitializingSingleton {

    private static final String ACTUATOR_MAPPINGS = "org.springframework.boot.webmvc.actuate.";

    private final ListableBeanFactory beans;
    private final RequestMappingHandlerMapping routes;
    private volatile List<HandlerMapping> others;

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
        beans.getBeansOfType(HandlerMapping.class).forEach((name, mapping) -> {
            if (mapping != routes) {
                problemOf(name, mapping).ifPresent(problems::add);
                mappings.add(mapping);
            }
        });
        if (!problems.isEmpty()) {
            throw new InvalidRouteException(problems);
        }
        AnnotationAwareOrderComparator.sort(mappings);
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
        var checked = others;
        if (checked == null) {
            return true;
        }
        return ParsedRequestPath.during(request, () -> checked.stream().anyMatch(mapping -> serves(mapping, request)));
    }

    private static boolean serves(HandlerMapping mapping, HttpServletRequest request) {
        try {
            return mapping.getHandler(request) != null;
        } catch (Exception cannotTell) {
            // HandlerMapping#getHandler declares Exception; a mapping that cannot answer may serve: fail closed.
            return true;
        }
    }

    private static Optional<String> problemOf(String name, HandlerMapping mapping) {
        if (mapping instanceof RouterFunctionMapping
                || mapping.getClass().getName().startsWith(ACTUATOR_MAPPINGS)) {
            return Optional.empty();
        }
        if (mapping instanceof AbstractUrlHandlerMapping urls) {
            var served = handledPaths(urls);
            return served.isEmpty()
                    ? Optional.empty()
                    : Optional.of("Handler mapping '" + name + "' serves " + served + " outside the"
                            + " annotated routes; declare them as route classes with @Access");
        }
        return Optional.of(
                "Handler mapping '" + name + "' (" + mapping.getClass().getName() + ") can serve"
                        + " requests outside the annotated routes; declare them as route classes with @Access");
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
