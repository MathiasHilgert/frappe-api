package com.frappe.platform;

import java.time.Duration;

/**
 * Stores short-lived, single-use secrets such as verification, reset and email-change codes. Only a salted Argon2 hash
 * is stored, so a dump of the store reveals no usable code.
 *
 * <p>A secret is consumed at most once and dies after {@value #MAX_FAILED_ATTEMPTS} wrong attempts, so a short numeric
 * code cannot be brute-forced. All instances share the store.
 *
 * <p>Every check costs one Argon2 run, also for a key without a secret (a dummy hash is verified), so timing does not
 * reveal which codes exist. That cost is also why callers must put {@link #consume} behind a {@link RateLimiter} check
 * (per account and per address): unlimited checks would let anyone spend the server's CPU.
 */
public interface ShortLivedSecretStore {

    /** Wrong attempts after which a secret is deleted. */
    int MAX_FAILED_ATTEMPTS = 5;

    /**
     * Stores a secret, replacing any earlier secret under the same key together with its failure count.
     *
     * @param key the secret's key
     * @param secret the plain secret; only its hash is stored
     * @param ttl how long the secret stays valid; at least 1 ms
     * @throws SecretStoreUnavailableException if the store cannot be reached
     */
    void put(SecretKey key, String secret, Duration ttl);

    /**
     * Checks a candidate and consumes the secret if it matches. A wrong candidate counts as a failed attempt; the
     * {@value #MAX_FAILED_ATTEMPTS}th failed attempt deletes the secret. Call it only after a {@link RateLimiter} allowed
     * the attempt.
     *
     * @param key the secret's key
     * @param candidate the value submitted by the user
     * @return {@code true} exactly once for a matching candidate; {@code false} for a wrong candidate, or when the
     *     secret expired, was consumed, deleted after too many failures, or never stored
     * @throws SecretStoreUnavailableException if the store cannot be reached
     */
    boolean consume(SecretKey key, String candidate);

    /**
     * Counts one issue of a secret against a sliding-window cap, e.g. at most 5 codes per hour per account. Callers ask
     * before issuing and issue only when allowed. An issue leaves the window once it is {@code window} old.
     *
     * @param key the secret's key; issues are counted per key
     * @param window length of the sliding window; at least 1 ms
     * @param limit issues allowed within any window; positive
     * @return {@code true} if the issue is within the cap and was counted; {@code false} if it was refused (and not
     *     counted)
     * @throws SecretStoreUnavailableException if the store cannot be reached
     */
    boolean countIssue(SecretKey key, Duration window, int limit);
}
