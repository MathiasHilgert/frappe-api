package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.Password;
import com.frappe.identity.domain.PasswordHash;
import com.frappe.identity.domain.PasswordHasher;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;

/**
 * Argon2id with Spring Security's v5.8 defaults. {@link #verifyDummy} verifies against a hash made once at startup
 * with the same encoder, so an unknown account costs the same Argon2 run as a known one.
 */
final class Argon2PasswordHasher implements PasswordHasher {

    // No password is ever this value after NFKC and the length rule would still hash it, but nothing depends on that:
    // the dummy hash is only compared, never stored.
    private static final String DUMMY_PASSWORD = "frappe-dummy-password-for-unknown-accounts";

    private final Argon2PasswordEncoder argon2 = Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8();

    private final PasswordHash dummyHash = new PasswordHash(argon2.encode(DUMMY_PASSWORD));

    /** Creates the hasher and its dummy hash (one Argon2 run). */
    Argon2PasswordHasher() {}

    @Override
    public PasswordHash hash(Password password) {
        return new PasswordHash(argon2.encode(password.value()));
    }

    @Override
    public boolean verify(Password password, PasswordHash hash) {
        return argon2.matches(password.value(), hash.value());
    }

    @Override
    public void verifyDummy(Password password) {
        argon2.matches(password.value(), dummyHash.value());
    }

    /**
     * The hash {@link #verifyDummy} compares against.
     *
     * @return the dummy hash
     */
    PasswordHash dummyHash() {
        return dummyHash;
    }
}
