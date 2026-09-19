package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Access;
import jakarta.servlet.http.HttpServletRequest;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.function.Predicate;
import java.util.stream.Collectors;
import org.springframework.core.annotation.AnnotatedElementUtils;
import org.springframework.web.method.HandlerTypePredicate;
import org.springframework.web.servlet.mvc.method.RequestMappingInfo;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;
import org.springframework.web.util.ServletRequestPathUtils;
import org.springframework.web.util.UrlPathHelper;

/**
 * The application's routes, checked at startup: every route class lives in a module's {@code infrastructure.web}
 * package, declares {@link Access} and maps exactly one method. Any violation fails startup with one {@link InvalidRouteException} naming every
 * offending class. Framework controllers (outside {@code com.frappe}) are not routes and are left alone.
 *
 * <p>The catalog also tells which handler serves a request, choosing among every annotated mapping exactly as Spring
 * MVC does (exact paths first, then the best match by MVC's own ordering), so the posture enforced before dispatch is
 * the posture of the handler that runs. Equally good matches are ambiguous: Spring MVC fails such a request, and the
 * catalog reports it as {@link RouteMatch.OtherHandler}, which the security chain refuses on purpose (fail closed).
 */
final class RouteCatalog {

    /** The package suffix every route class lives in: {@code com.frappe.<module>.infrastructure.web}. */
    static final String ROUTE_PACKAGE_SUFFIX = ".infrastructure.web";

    /** Handler types that are application routes: every controller under {@code com.frappe}. */
    static final Predicate<Class<?>> APPLICATION_ROUTES = HandlerTypePredicate.forBasePackage("com.frappe");

    private final List<Route> routes;
    private final Map<RequestMappingInfo, Route> routesByMapping;
    private final List<RequestMappingInfo> allMappings;
    private final Map<String, List<RequestMappingInfo>> mappingsByDirectPath;

    /**
     * Reads and checks the routes registered with Spring MVC.
     *
     * @param mappings Spring MVC's annotated handler mappings
     * @throws InvalidRouteException when a route class breaks a rule
     */
    RouteCatalog(RequestMappingHandlerMapping mappings) {
        var problems = new ArrayList<String>();
        var checked = new ArrayList<Route>();
        routeMethodsByType(mappings).forEach((type, methods) -> {
            var access = AnnotatedElementUtils.findMergedAnnotation(type, Access.class);
            var typeProblems = problemsOf(type, methods.size(), access);
            problems.addAll(typeProblems);
            if (typeProblems.isEmpty()) {
                checked.add(new Route(methods.getFirst(), access.value()));
            }
        });
        if (!problems.isEmpty()) {
            throw new InvalidRouteException(problems);
        }
        this.routes = List.copyOf(checked);
        this.routesByMapping = checked.stream().collect(Collectors.toUnmodifiableMap(Route::mapping, route -> route));
        this.allMappings = List.copyOf(mappings.getHandlerMethods().keySet());
        this.mappingsByDirectPath = allMappings.stream()
                .flatMap(mapping -> mapping.getDirectPaths().stream().map(path -> Map.entry(path, mapping)))
                .collect(Collectors.groupingBy(
                        Map.Entry::getKey, Collectors.mapping(Map.Entry::getValue, Collectors.toUnmodifiableList())));
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
     * Which handler serves a request ({@code HandlerMappingIntrospector}, Spring's own answer, is deprecated for
     * removal in Spring 7).
     *
     * @param request the incoming request
     * @return the serving application route, another handler (framework controller or ambiguous match), or none
     */
    RouteMatch match(HttpServletRequest request) {
        return ParsedRequestPath.during(request, () -> bestMatch(request));
    }

    private RouteMatch bestMatch(HttpServletRequest request) {
        // As AbstractHandlerMethodMapping#lookupHandlerMethod: mappings of the exact path first, all of them otherwise.
        var matches = matching(mappingsByDirectPath.getOrDefault(lookupPath(request), List.of()), request);
        if (matches.isEmpty()) {
            matches = matching(allMappings, request);
        }
        if (matches.isEmpty()) {
            return new RouteMatch.NoHandler();
        }
        Comparator<Map.Entry<RequestMappingInfo, RequestMappingInfo>> bySpecificity =
                (first, second) -> first.getValue().compareTo(second.getValue(), request);
        matches.sort(bySpecificity);
        if (matches.size() > 1 && bySpecificity.compare(matches.get(0), matches.get(1)) == 0) {
            return new RouteMatch.OtherHandler();
        }
        var route = routesByMapping.get(matches.getFirst().getKey());
        return route == null ? new RouteMatch.OtherHandler() : new RouteMatch.ApplicationRoute(route);
    }

    private static List<Map.Entry<RequestMappingInfo, RequestMappingInfo>> matching(
            List<RequestMappingInfo> mappings, HttpServletRequest request) {
        var matches = new ArrayList<Map.Entry<RequestMappingInfo, RequestMappingInfo>>();
        for (var mapping : mappings) {
            var condition = mapping.getMatchingCondition(request);
            if (condition != null) {
                matches.add(Map.entry(mapping, condition));
            }
        }
        return matches;
    }

    private static String lookupPath(HttpServletRequest request) {
        var path = ServletRequestPathUtils.getParsedRequestPath(request)
                .pathWithinApplication()
                .value();
        return UrlPathHelper.defaultInstance.removeSemicolonContent(path);
    }

    private static Map<Class<?>, List<RequestMappingInfo>> routeMethodsByType(RequestMappingHandlerMapping mappings) {
        // Sorted by class name, so the startup failure lists the problems in a stable order.
        var methodsByType = new TreeMap<Class<?>, List<RequestMappingInfo>>(Comparator.comparing(Class::getName));
        mappings.getHandlerMethods().forEach((mapping, method) -> {
            if (APPLICATION_ROUTES.test(method.getBeanType())) {
                methodsByType
                        .computeIfAbsent(method.getBeanType(), type -> new ArrayList<>())
                        .add(mapping);
            }
        });
        return methodsByType;
    }

    private static List<String> problemsOf(Class<?> type, int mappedMethods, Access access) {
        var problems = new ArrayList<String>();
        if (mappedMethods != 1) {
            problems.add(type.getName() + " has " + mappedMethods + " mapped methods; a route is one class with"
                    + " exactly one mapped method, so split it into one class per route");
        }
        if (!type.getPackageName().endsWith(ROUTE_PACKAGE_SUFFIX)) {
            problems.add(type.getName() + " lives in " + type.getPackageName() + "; routes are adapters and belong"
                    + " in the module's infrastructure.web package");
        }
        if (access == null) {
            problems.add(type.getName() + " declares no @Access; annotate the class with @Access(Posture.…) to"
                    + " state who may call it");
        }
        return problems;
    }
}
