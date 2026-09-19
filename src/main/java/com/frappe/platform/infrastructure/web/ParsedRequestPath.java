package com.frappe.platform.infrastructure.web;

import jakarta.servlet.http.HttpServletRequest;
import java.util.function.Supplier;
import org.springframework.web.util.ServletRequestPathUtils;

/**
 * Runs handler lookups before Spring MVC dispatches. Mapping conditions read the parsed request path that Spring MVC
 * caches only once it dispatches; it is parsed here and removed again, so dispatching starts from a clean request, as
 * Spring Security's {@code PathPatternRequestMatcher} does.
 */
final class ParsedRequestPath {

    private ParsedRequestPath() {}

    /**
     * Runs a lookup with the request path parsed.
     *
     * @param request the incoming request
     * @param lookup the lookup reading mapping conditions
     * @param <T> the lookup result
     * @return the lookup result
     */
    static <T> T during(HttpServletRequest request, Supplier<T> lookup) {
        var parsedHere = !ServletRequestPathUtils.hasParsedRequestPath(request);
        if (parsedHere) {
            ServletRequestPathUtils.parseAndCache(request);
        }
        try {
            return lookup.get();
        } finally {
            if (parsedHere) {
                ServletRequestPathUtils.clearParsedRequestPath(request);
            }
        }
    }
}
