package com.frappe.platform.web;

import java.util.Objects;
import java.util.UUID;

/**
 * The session a bearer token stands for: the caller of a request. Routes read it with
 * {@code @AuthenticationPrincipal ResolvedSession session}.
 *
 * @param principalId the principal acting through the session
 * @param sessionId the session itself
 * @param kind who stands behind the session
 */
public record ResolvedSession(UUID principalId, UUID sessionId, SessionKind kind) {

    /**
     * Creates a resolved session.
     *
     * @param principalId the principal acting through the session
     * @param sessionId the session itself
     * @param kind who stands behind the session
     */
    public ResolvedSession {
        Objects.requireNonNull(principalId, "principalId");
        Objects.requireNonNull(sessionId, "sessionId");
        Objects.requireNonNull(kind, "kind");
    }
}
