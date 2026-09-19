package com.frappe.identity.domain;

import java.util.Optional;
import java.util.UUID;

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
     * Finds the sign-up of an address.
     *
     * @param emailSubject the keyed digest of the canonical address
     * @return the sign-up, or empty when the address has none
     */
    Optional<SignUp> byEmailSubject(UUID emailSubject);

    /**
     * Inserts a new sign-up or updates a stored one.
     *
     * @param signUp the sign-up
     */
    void save(SignUp signUp);
}
