package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletRequest;
import java.util.Optional;
import java.util.UUID;
import java.util.regex.Pattern;
import org.springframework.web.util.ServletRequestPathUtils;
import org.springframework.web.util.pattern.PathPattern;

/**
 * The path convention of business-scoped routes: {@code /v1/businesses/{businessId}} and everything under it. The
 * business is read from the route Spring MVC dispatches to, so a route that is business-scoped can never run without
 * its business being checked.
 */
final class BusinessPath {

    /** A route pattern whose first variable, right after {@code /v1/businesses/}, names the business. */
    private static final Pattern SCOPED_ROUTE = Pattern.compile("^/v1/businesses/\\{([^/{}]+)}(/.*)?$");

    /** A canonical UUID: 36 characters, hyphens in place; {@link UUID#fromString} alone accepts shorter forms. */
    private static final Pattern CANONICAL_UUID =
            Pattern.compile("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private BusinessPath() {}

    /**
     * The business segment of a request, when the route serving it is business-scoped.
     *
     * @param route the route that serves the request
     * @param request the request
     * @return the segment as the client sent it (decoded, possibly malformed), or empty for a route outside the path
     */
    static Optional<String> segmentOf(Route route, HttpServletRequest request) {
        return ParsedRequestPath.during(request, () -> {
            var matching = route.mapping().getMatchingCondition(request);
            if (matching == null || matching.getPathPatternsCondition() == null) {
                return Optional.empty();
            }
            for (PathPattern pattern : matching.getPathPatternsCondition().getPatterns()) {
                var scoped = SCOPED_ROUTE.matcher(pattern.getPatternString());
                if (!scoped.matches()) {
                    continue;
                }
                var path = ServletRequestPathUtils.getParsedRequestPath(request).pathWithinApplication();
                var variables = pattern.matchAndExtract(path);
                if (variables != null) {
                    return Optional.ofNullable(variables.getUriVariables().get(scoped.group(1)));
                }
            }
            return Optional.empty();
        });
    }

    /**
     * Parses a business segment.
     *
     * @param segment the segment of the path
     * @return the business id, or empty when the segment is not a canonical UUID
     */
    static Optional<UUID> businessIdOf(String segment) {
        return CANONICAL_UUID.matcher(segment).matches() ? Optional.of(UUID.fromString(segment)) : Optional.empty();
    }
}
