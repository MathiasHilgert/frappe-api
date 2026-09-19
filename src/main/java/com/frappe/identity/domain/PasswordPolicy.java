package com.frappe.identity.domain;

import com.frappe.identity.domain.PasswordRejected.Reason;
import com.frappe.platform.Result;
import java.util.Objects;

/**
 * The one password policy for every password identity stores (sign-up, reset, change, staff): the length rule of
 * {@link Password}, then the breach check. One policy, because the weakest path would become the attackers' path.
 *
 * <p>The breach check fails open: when the corpus cannot be asked, the password is accepted, so a third-party outage
 * never stops sign-ups; the length rule still holds.
 */
public final class PasswordPolicy {

    private final BreachedPasswords breachedPasswords;

    /**
     * Creates the policy.
     *
     * @param breachedPasswords the breach corpus
     */
    public PasswordPolicy(BreachedPasswords breachedPasswords) {
        this.breachedPasswords = Objects.requireNonNull(breachedPasswords, "breachedPasswords");
    }

    /**
     * Checks a password as typed.
     *
     * @param raw the password as typed
     * @return the normalized password, or why it was refused
     */
    public Result<Password, PasswordRejected> check(String raw) {
        return Password.of(raw).flatMap(this::notBreached);
    }

    private Result<Password, PasswordRejected> notBreached(Password password) {
        if (breachedPasswords.check(password) == BreachStatus.BREACHED) {
            return Result.failure(new PasswordRejected(Reason.BREACHED));
        }
        return Result.success(password);
    }
}
