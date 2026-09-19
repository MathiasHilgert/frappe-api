package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.Secrets;
import java.security.SecureRandom;
import java.util.Base64;

/** Secrets from {@link SecureRandom}. */
final class SecureRandomSecrets implements Secrets {

    private static final int TOKEN_BYTES = 32;

    private static final int CODE_BOUND = 1_000_000;

    private static final String CODE_FORMAT = "%06d";

    private static final String CROCKFORD_BASE32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static final int RECOVERY_CODE_LENGTH = 10;

    private final SecureRandom random;

    /**
     * Creates the source.
     *
     * @param random the strong random source
     */
    SecureRandomSecrets(SecureRandom random) {
        this.random = random;
    }

    @Override
    public String token() {
        var bytes = new byte[TOKEN_BYTES];
        random.nextBytes(bytes);
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    @Override
    public String code() {
        return CODE_FORMAT.formatted(random.nextInt(CODE_BOUND));
    }

    @Override
    public String recoveryCode() {
        var code = new StringBuilder(RECOVERY_CODE_LENGTH);
        for (var i = 0; i < RECOVERY_CODE_LENGTH; i++) {
            // 32 symbols: a power of two, so every symbol is equally likely.
            code.append(CROCKFORD_BASE32.charAt(random.nextInt(CROCKFORD_BASE32.length())));
        }
        return code.toString();
    }
}
