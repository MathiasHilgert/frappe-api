package com.frappe.platform.web;

import java.util.Objects;
import java.util.Optional;
import java.util.UUID;

/**
 * The session a bearer token stands for: the caller of a request. An {@code AUTHENTICATED} route receives it by
 * declaring a {@code ResolvedSession} parameter and passes it on to its use case, which authorizes the operation.
 *
 * @param principalId the principal acting through the session
 * @param sessionId the session itself
 * @param kind who stands behind the session
 * @param businessId the business a staff, terminal or guest session belongs to; empty for a person
 * @param branchId the branch a staff, terminal or guest session belongs to, if any
 */
public record ResolvedSession(
        UUID principalId, UUID sessionId, SessionKind kind, Optional<UUID> businessId, Optional<UUID> branchId) {

    /**
     * Creates a resolved session.
     *
     * @param principalId the principal acting through the session
     * @param sessionId the session itself
     * @param kind who stands behind the session
     * @param businessId the business a staff, terminal or guest session belongs to; empty for a person
     * @param branchId the branch a staff, terminal or guest session belongs to, if any
     */
    public ResolvedSession {
        Objects.requireNonNull(principalId, "principalId");
        Objects.requireNonNull(sessionId, "sessionId");
        Objects.requireNonNull(kind, "kind");
        Objects.requireNonNull(businessId, "businessId");
        Objects.requireNonNull(branchId, "branchId");
        if (kind == SessionKind.PERSON && businessId.isPresent()) {
            throw new IllegalArgumentException("A PERSON session is bound to no business; it enters one per request");
        }
        if (branchId.isPresent() && businessId.isEmpty()) {
            throw new IllegalArgumentException("A session bound to a branch is bound to its business too");
        }
    }

    /**
     * Creates a session bound to no business, such as a person's.
     *
     * @param principalId the principal acting through the session
     * @param sessionId the session itself
     * @param kind who stands behind the session
     */
    public ResolvedSession(UUID principalId, UUID sessionId, SessionKind kind) {
        this(principalId, sessionId, kind, Optional.empty(), Optional.empty());
    }
}
