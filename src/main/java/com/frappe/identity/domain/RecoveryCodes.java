package com.frappe.identity.domain;

import com.frappe.platform.KeyedDigests;
import java.util.LinkedHashSet;
import java.util.List;

/**
 * Issues a person's recovery codes: {@value #COUNT} distinct codes, shown once, stored only as keyed digests bound to
 * the person, so a database dump yields no usable code and a digest of one person is worthless for another.
 */
public final class RecoveryCodes {

    /** How many codes a person holds. */
    public static final int COUNT = 8;

    /** The {@link KeyedDigests} namespace of recovery codes. */
    public static final String DIGEST_NAMESPACE = "identity.recovery-code";

    private RecoveryCodes() {}

    /**
     * Draws {@value #COUNT} distinct codes and their digests.
     *
     * @param person whose codes they are
     * @param secrets the random source
     * @param digests the keyed digests
     * @return the codes to show and their digests to store, in the same order
     */
    public static Issued issue(PersonId person, Secrets secrets, KeyedDigests digests) {
        var codes = new LinkedHashSet<String>();
        while (codes.size() < COUNT) {
            codes.add(secrets.recoveryCode());
        }
        var digested = codes.stream().map(code -> digestOf(digests, person, code)).toList();
        return new Issued(List.copyOf(codes), digested);
    }

    /**
     * The digest stored for a person's code.
     *
     * @param digests the keyed digests
     * @param person whose code it is
     * @param code the code as shown
     * @return the digest
     */
    public static String digestOf(KeyedDigests digests, PersonId person, String code) {
        return digests.digestOf(DIGEST_NAMESPACE, person.value() + ":" + code);
    }

    /**
     * Freshly issued codes.
     *
     * @param codes the codes to show once
     * @param digests their digests to store, in the same order
     */
    public record Issued(List<String> codes, List<String> digests) {

        /**
         * Copies the lists.
         *
         * @param codes the codes to show once
         * @param digests their digests, in the same order
         */
        public Issued {
            codes = List.copyOf(codes);
            digests = List.copyOf(digests);
        }

        @Override
        public String toString() {
            return "Issued[<redacted>]";
        }
    }
}
