package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.ResolvedSession;
import org.springframework.security.authentication.AbstractAuthenticationToken;
import org.springframework.security.core.authority.AuthorityUtils;

/**
 * An authenticated caller: the session a bearer token resolved to. It carries no credentials (the token is not kept)
 * and no authorities (authorization is decided by the use case, not the HTTP layer).
 */
final class SessionAuthentication extends AbstractAuthenticationToken {

    private static final long serialVersionUID = 1L;

    private final ResolvedSession session;

    /**
     * Creates the authentication of a resolved session.
     *
     * @param session the caller's session
     */
    SessionAuthentication(ResolvedSession session) {
        super(AuthorityUtils.NO_AUTHORITIES);
        this.session = session;
        super.setAuthenticated(true);
    }

    /**
     * The caller's session, injected into routes as a plain {@code ResolvedSession} parameter.
     *
     * @return the session
     */
    @Override
    public ResolvedSession getPrincipal() {
        return session;
    }

    /**
     * Always {@code null}: the bearer token is never kept after it resolved.
     *
     * @return {@code null}
     */
    @Override
    public Object getCredentials() {
        return null;
    }

    /**
     * The principal id, the caller's stable name.
     *
     * @return the principal id as text
     */
    @Override
    public String getName() {
        return session.principalId().toString();
    }
}
