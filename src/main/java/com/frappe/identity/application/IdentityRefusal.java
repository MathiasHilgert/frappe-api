package com.frappe.identity.application;

import com.frappe.identity.domain.PasswordRejected;
import java.util.Objects;

/** Why identity refused a request: the business failures its use cases return. */
public sealed interface IdentityRefusal {

    /** A rate limit refused the attempt. */
    record TooManyAttempts() implements IdentityRefusal {}

    /** The code is wrong, expired, used, replaced or was never issued; callers cannot tell which. */
    record InvalidCode() implements IdentityRefusal {}

    /**
     * The password policy refused the password.
     *
     * @param reason the rule it broke
     */
    record PasswordRefused(PasswordRejected.Reason reason) implements IdentityRefusal {

        /**
         * Validates the reason.
         *
         * @param reason the rule it broke, not {@code null}
         */
        public PasswordRefused {
            Objects.requireNonNull(reason, "reason");
        }
    }

    /** A given or family name is empty or too long. */
    record InvalidName() implements IdentityRefusal {}

    /** The accepted terms or privacy policy version is not the current one. */
    record LegalTermsOutdated() implements IdentityRefusal {}

    /** Another person registered the address first. */
    record EmailAlreadyRegistered() implements IdentityRefusal {}
}
