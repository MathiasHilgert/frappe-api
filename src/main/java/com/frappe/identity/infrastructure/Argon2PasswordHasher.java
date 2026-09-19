package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.Password;
import com.frappe.identity.domain.PasswordHash;
import com.frappe.identity.domain.PasswordHasher;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * Argon2id with OWASP's baseline parameters: 19 MiB of memory, 2 iterations, parallelism 1, a 16-byte salt and a
 * 32-byte hash. The parameters are encoded in every hash, so hashes made with other parameters stay verifiable. {@link #verifyDummy} verifies against a hash made once at startup
 * with the same encoder, so an unknown account costs the same Argon2 run as a known one.
 */
final class Argon2PasswordHasher implements PasswordHasher {

    // No password is ever this value after NFKC and the length rule would still hash it, but nothing depends on that:
    // the dummy hash is only compared, never stored.
    private static final String DUMMY_PASSWORD = "frappe-dummy-password-for-unknown-accounts";

    private static final int SALT_LENGTH = 16;

    private static final int HASH_LENGTH = 32;

    private static final int PARALLELISM = 1;

    private static final int MEMORY_KIB = 19 * 1024;

    private static final int ITERATIONS = 2;

    private final PasswordEncoder argon2;

    private final PasswordHash dummyHash;

    /** Creates the hasher and its dummy hash (one Argon2 run). */
    Argon2PasswordHasher() {
        this(new Argon2PasswordEncoder(SALT_LENGTH, HASH_LENGTH, PARALLELISM, MEMORY_KIB, ITERATIONS));
    }

    /**
     * Creates the hasher over an encoder; tests pass a counting one.
     *
     * @param argon2 the Argon2 encoder, used for hashing, verifying and the dummy hash alike
     */
    Argon2PasswordHasher(PasswordEncoder argon2) {
        this.argon2 = argon2;
        this.dummyHash = new PasswordHash(argon2.encode(DUMMY_PASSWORD));
    }

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
