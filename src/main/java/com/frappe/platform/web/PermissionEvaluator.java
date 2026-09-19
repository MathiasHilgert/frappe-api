package com.frappe.platform.web;

/**
 * Decides whether a session holds a permission, for routes with {@link Posture#PERMISSION}; implemented by the access
 * module. Without an implementation every permission is refused (403).
 */
@FunctionalInterface
public interface PermissionEvaluator {

    /**
     * Checks a permission.
     *
     * @param session the caller
     * @param permission the permission the route declares in {@link Access#permission()}
     * @return whether the caller holds the permission
     */
    boolean isPermitted(ResolvedSession session, String permission);
}
