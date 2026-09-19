package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.ResolvedSession;
import org.springframework.core.MethodParameter;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.bind.support.WebDataBinderFactory;
import org.springframework.web.context.request.NativeWebRequest;
import org.springframework.web.method.support.HandlerMethodArgumentResolver;
import org.springframework.web.method.support.ModelAndViewContainer;

/**
 * Injects the caller into route methods as a plain {@link ResolvedSession} parameter, so route classes never depend on
 * Spring Security types. Only {@code AUTHENTICATED} routes may declare it (checked at startup), and their posture
 * guarantees a session before the route runs.
 */
final class ResolvedSessionArgumentResolver implements HandlerMethodArgumentResolver {

    /** Creates the resolver. */
    ResolvedSessionArgumentResolver() {}

    @Override
    public boolean supportsParameter(MethodParameter parameter) {
        return parameter.getParameterType() == ResolvedSession.class;
    }

    @Override
    public ResolvedSession resolveArgument(
            MethodParameter parameter,
            ModelAndViewContainer mavContainer,
            NativeWebRequest webRequest,
            WebDataBinderFactory binderFactory) {
        if (SecurityContextHolder.getContextHolderStrategy().getContext().getAuthentication()
                instanceof SessionAuthentication caller) {
            return caller.getPrincipal();
        }
        // Unreachable while the posture check holds: only AUTHENTICATED routes may declare the parameter.
        throw new IllegalStateException("No resolved session for " + parameter.getExecutable()
                + "; only AUTHENTICATED routes may declare a ResolvedSession parameter");
    }
}
