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

    /** Every path under it is business-scoped. */
    static final String PREFIX = "/v1/businesses/";

    /**
     * The one shape of a business-scoped route pattern (and of its path in the OpenAPI spec): a single-segment
     * variable right after {@link #PREFIX}, then nothing or more segments. No catch-all, regex or partial segment.
     */
    static final Pattern SCOPED_ROUTE = Pattern.compile("^/v1/businesses/\\{([A-Za-z_][A-Za-z0-9_]*)}(/.*)?$");

    /** A canonical UUID: 36 characters, hyphens in place; {@link UUID#fromString} alone accepts shorter forms. */
    private static final Pattern CANONICAL_UUID =
            Pattern.compile("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private BusinessPath() {}

    /**
     * Tells whether a route pattern breaks the business path convention.
     *
     * @param pattern a route pattern, including the {@code /v1} prefix
     * @return {@code true} when it lies under {@link #PREFIX} without the one allowed shape
     */
    static boolean breaksTheConvention(String pattern) {
        return pattern.startsWith(PREFIX) && !SCOPED_ROUTE.matcher(pattern).matches();
    }

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
                if (!pattern.getPatternString().startsWith(PREFIX)) {
                    continue;
                }
                // Fail closed: a scoped pattern that yields no business segment (refused at startup anyway) yields an
                // empty segment, which is not a business id.
                var scoped = SCOPED_ROUTE.matcher(pattern.getPatternString());
                var path = ServletRequestPathUtils.getParsedRequestPath(request).pathWithinApplication();
                var variables = pattern.matchAndExtract(path);
                if (!scoped.matches() || variables == null) {
                    return Optional.of("");
                }
                return Optional.of(variables.getUriVariables().getOrDefault(scoped.group(1), ""));
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
