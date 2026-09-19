package com.frappe.identity.domain;

import java.util.Optional;

/** Port: the stored sign-ups. */
public interface SignUps {

    /**
     * Finds a sign-up.
     *
     * @param id its id
     * @return the sign-up, or empty when there is none
     */
    Optional<SignUp> byId(SignUpId id);

    /**
     * Records a start in one step: inserts the sign-up, or, when its address already has one, replaces that one's
     * address as entered, language and start time and raises its version. Concurrent starts for one address never
     * conflict.
     *
     * @param start the sign-up to record
     * @return the stored sign-up: the address's existing id, and the version this start produced
     */
    SignUp record(SignUp start);
}
