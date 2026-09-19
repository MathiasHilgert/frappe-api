package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import jakarta.servlet.http.HttpServletRequest;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.Predicate;
import java.util.stream.Collectors;
import org.springframework.core.annotation.AnnotatedElementUtils;
import org.springframework.web.method.HandlerMethod;
import org.springframework.web.method.HandlerTypePredicate;
import org.springframework.web.servlet.mvc.method.RequestMappingInfo;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;
import org.springframework.web.util.ServletRequestPathUtils;

/**
 * The application's routes, checked at startup: every route class declares {@link Access} with a consistent posture
 * and maps exactly one method. Any violation fails startup with one {@link InvalidRouteException} naming every
 * offending class. Framework controllers (outside {@code com.frappe}) are not routes and are left alone.
 *
 * <p>The catalog also tells which route serves a request, choosing among every annotated mapping exactly as Spring
 * MVC does, so the posture enforced before dispatch is the posture of the handler that runs.
 */
final class RouteCatalog {

    /** Handler types that are application routes: every controller under {@code com.frappe}. */
    static final Predicate<Class<?>> APPLICATION_ROUTES = HandlerTypePredicate.forBasePackage("com.frappe");

    private final List<Route> routes;
    private final Map<RequestMappingInfo, Route> routesByMapping;
    private final List<RequestMappingInfo> allMappings;

    /**
     * Reads and checks the routes registered with Spring MVC.
     *
     * @param mappings Spring MVC's annotated handler mappings
     * @throws InvalidRouteException when a route class breaks a rule
     */
    RouteCatalog(RequestMappingHandlerMapping mappings) {
        var methodsByType = new LinkedHashMap<Class<?>, List<Map.Entry<RequestMappingInfo, HandlerMethod>>>();
        mappings.getHandlerMethods().entrySet().stream()
                .filter(entry -> APPLICATION_ROUTES.test(entry.getValue().getBeanType()))
                .sorted(Comparator.comparing(
                        entry -> entry.getValue().getBeanType().getName()))
                .forEach(entry -> methodsByType
                        .computeIfAbsent(entry.getValue().getBeanType(), type -> new ArrayList<>())
                        .add(entry));
        var problems = new ArrayList<String>();
        var checked = new ArrayList<Route>();
        methodsByType.forEach((type, methods) -> {
            var access = AnnotatedElementUtils.findMergedAnnotation(type, Access.class);
            var typeProblems = problemsOf(type, methods.size(), access);
            problems.addAll(typeProblems);
            if (typeProblems.isEmpty()) {
                checked.add(new Route(methods.getFirst().getKey(), type, access.value(), access.permission()));
            }
        });
        if (!problems.isEmpty()) {
            throw new InvalidRouteException(problems);
        }
        this.routes = List.copyOf(checked);
        this.routesByMapping = checked.stream().collect(Collectors.toUnmodifiableMap(Route::mapping, route -> route));
        this.allMappings = List.copyOf(mappings.getHandlerMethods().keySet());
    }

    /**
     * The checked routes.
     *
     * @return every application route, one per route class
     */
    List<Route> routes() {
        return routes;
    }

    /**
     * The route that serves a request: the best of all matching mappings by Spring MVC's own ordering (its
     * {@code HandlerMappingIntrospector} is deprecated for removal in Spring 7).
     *
     * @param request the incoming request
     * @return the serving route; empty when no route, a framework controller, or several equally good mappings match
     *     (Spring MVC fails such a request as ambiguous)
     */
    Optional<Route> routeFor(HttpServletRequest request) {
        // Path conditions read the parsed path Spring MVC caches only once it dispatches; parse it here and remove it
        // again so dispatching starts from a clean request, as Spring Security's PathPatternRequestMatcher does.
        var parsedHere = !ServletRequestPathUtils.hasParsedRequestPath(request);
        if (parsedHere) {
            ServletRequestPathUtils.parseAndCache(request);
        }
        try {
            return bestMatch(request).map(routesByMapping::get);
        } finally {
            if (parsedHere) {
                ServletRequestPathUtils.clearParsedRequestPath(request);
            }
        }
    }

    private Optional<RequestMappingInfo> bestMatch(HttpServletRequest request) {
        var matches = new ArrayList<Map.Entry<RequestMappingInfo, RequestMappingInfo>>();
        for (var mapping : allMappings) {
            var condition = mapping.getMatchingCondition(request);
            if (condition != null) {
                matches.add(Map.entry(mapping, condition));
            }
        }
        Comparator<Map.Entry<RequestMappingInfo, RequestMappingInfo>> bySpecificity =
                (first, second) -> first.getValue().compareTo(second.getValue(), request);
        matches.sort(bySpecificity);
        if (matches.isEmpty() || (matches.size() > 1 && bySpecificity.compare(matches.get(0), matches.get(1)) == 0)) {
            return Optional.empty();
        }
        return Optional.of(matches.getFirst().getKey());
    }

    private static List<String> problemsOf(Class<?> type, int mappedMethods, Access access) {
        var problems = new ArrayList<String>();
        if (mappedMethods != 1) {
            problems.add(type.getName() + " has " + mappedMethods + " mapped methods; a route is one class with"
                    + " exactly one mapped method, so split it into one class per route");
        }
        if (access == null) {
            problems.add(type.getName() + " declares no @Access; annotate the class with @Access(Posture.…) to"
                    + " state who may call it");
        } else if (access.value() == Posture.PERMISSION && access.permission().isBlank()) {
            problems.add(type.getName() + " has posture PERMISSION but names no permission; declare"
                    + " @Access(value = Posture.PERMISSION, permission = \"…\")");
        } else if (access.value() != Posture.PERMISSION && !access.permission().isEmpty()) {
            problems.add(type.getName() + " names permission '" + access.permission() + "' but posture "
                    + access.value() + " checks no permission; use Posture.PERMISSION or remove the permission");
        }
        return problems;
    }
}
