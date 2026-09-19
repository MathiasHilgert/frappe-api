package com.frappe.platform.web;

import java.util.Objects;
import java.util.UUID;

/**
 * The session a bearer token stands for: the caller of a request. An {@code AUTHENTICATED} route receives it by
 * declaring a {@code ResolvedSession} parameter and passes it on to its use case, which authorizes the operation.
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
