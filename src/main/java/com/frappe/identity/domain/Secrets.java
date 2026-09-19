package com.frappe.identity.domain;

/** Port: random secrets from a cryptographically strong source. */
public interface Secrets {

    /**
     * A bearer token: 32 random bytes, base64url without padding.
     *
     * @return 43 characters
     */
    String token();

    /**
     * A one-time code typed from a mail.
     *
     * @return 6 digits, leading zeros kept
     */
    String code();

    /**
     * An account recovery code: 50 random bits in Crockford base32.
     *
     * @return 10 characters of {@code 0-9A-Z} without {@code I}, {@code L}, {@code O} and {@code U}
     */
    String recoveryCode();
}
