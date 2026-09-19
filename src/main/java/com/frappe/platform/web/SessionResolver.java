package com.frappe.platform.web;

import java.util.Optional;

/**
 * Resolves the bearer token of a request to its session; implemented by the identity module. The token is read from
 * the {@code Authorization: Bearer} header only, before any route runs. Without an implementation no token resolves,
 * so every route that is not {@link Posture#PUBLIC} answers 401.
 */
@FunctionalInterface
public interface SessionResolver {

    /**
     * Resolves a bearer token.
     *
     * @param bearerToken the token exactly as sent, syntactically valid per RFC 6750; never logged
     * @return the live session the token stands for, empty when it stands for none (unknown, expired or revoked)
     */
    Optional<ResolvedSession> resolve(String bearerToken);
}
