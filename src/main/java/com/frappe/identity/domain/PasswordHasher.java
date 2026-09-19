package com.frappe.identity.domain;

/** Port: hashes and verifies passwords with a slow, salted algorithm. */
public interface PasswordHasher {

    /**
     * Hashes a password with a fresh salt.
     *
     * @param password the password
     * @return the encoded hash
     */
    PasswordHash hash(Password password);

    /**
     * Checks a password against a stored hash.
     *
     * @param password the password as given
     * @param hash the stored hash
     * @return whether they match
     */
    boolean verify(Password password, PasswordHash hash);

    /**
     * Does the work of {@link #verify} with the same parameters against a hash no password matches, for an unknown
     * account, so its answer takes as long as a known one's.
     *
     * @param password the password as given
     */
    void verifyDummy(Password password);
}
