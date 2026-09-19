package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.IdGenerator;
import com.frappe.platform.SecretKey;
import com.frappe.platform.SecretStoreUnavailableException;
import com.frappe.platform.ShortLivedSecretStore;
import java.time.Clock;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import org.springframework.core.io.ClassPathResource;
import org.springframework.dao.DataAccessException;
import org.springframework.data.redis.core.RedisOperations;
import org.springframework.data.redis.core.SessionCallback;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.RedisScript;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * {@link ShortLivedSecretStore} in Valkey. A secret is one hash with its Argon2 hash and failure count, expiring with the
 * secret. The candidate is verified in the application (the hash is salted); a Lua script then settles the attempt
 * atomically, so concurrent right submissions succeed once and concurrent wrong ones are all counted.
 *
 * <p>Issue caps are a sliding-window log (a sorted set of issue times, trimmed and checked by a Lua script). Times come
 * from the application clock; instances must keep their clocks in sync (NTP), as for every other timestamp.
 */
final class ValkeyShortLivedSecretStore implements ShortLivedSecretStore {

    private static final String HASH_FIELD = "hash";

    private static final String FAILURES_FIELD = "failures";

    private static final String MATCHED = "1";

    private static final String NOT_MATCHED = "0";

    /** What both scripts return when they consumed the secret or recorded the issue. */
    private static final long RECORDED = 1;

    private static final RedisScript<Long> CONSUME = script("consume-secret.lua");

    private static final RedisScript<Long> COUNT_ISSUE = script("count-issue.lua");

    private final StringRedisTemplate redis;

    private final PasswordEncoder hashes;

    private final Clock clock;

    private final IdGenerator ids;

    /**
     * Creates the store.
     *
     * @param redis the Valkey client
     * @param hashes the one-way encoder for stored secrets (Argon2)
     * @param clock the application clock, for issue times
     * @param ids unique members for the issue log
     */
    ValkeyShortLivedSecretStore(StringRedisTemplate redis, PasswordEncoder hashes, Clock clock, IdGenerator ids) {
        this.redis = redis;
        this.hashes = hashes;
        this.clock = clock;
        this.ids = ids;
    }

    @Override
    public void put(SecretKey key, String secret, Duration ttl) {
        Objects.requireNonNull(key, "key");
        requireSecret(secret);
        requirePositive(ttl, "ttl");
        var hash = hashes.encode(secret);
        var redisKey = ValkeyKeys.secret(key);
        try {
            redis.execute(replaceSecret(redisKey, hash, ttl));
        } catch (DataAccessException e) {
            throw new SecretStoreUnavailableException("Storing a secret failed: Valkey is unavailable", e);
        }
    }

    @Override
    public boolean consume(SecretKey key, String candidate) {
        Objects.requireNonNull(key, "key");
        Objects.requireNonNull(candidate, "candidate");
        var redisKey = ValkeyKeys.secret(key);
        try {
            var stored = redis.<String, String>opsForHash().get(redisKey, HASH_FIELD);
            if (stored == null) {
                return false;
            }
            var outcome = hashes.matches(candidate, stored) ? MATCHED : NOT_MATCHED;
            var consumed =
                    redis.execute(CONSUME, List.of(redisKey), stored, outcome, String.valueOf(MAX_FAILED_ATTEMPTS));
            return Long.valueOf(RECORDED).equals(consumed);
        } catch (DataAccessException e) {
            throw new SecretStoreUnavailableException("Checking a secret failed: Valkey is unavailable", e);
        }
    }

    @Override
    public boolean countIssue(SecretKey key, Duration window, int limit) {
        Objects.requireNonNull(key, "key");
        requirePositive(window, "window");
        if (limit < 1) {
            throw new IllegalArgumentException("limit must be positive, was " + limit);
        }
        try {
            var recorded = redis.execute(
                    COUNT_ISSUE,
                    List.of(ValkeyKeys.secretIssues(key)),
                    String.valueOf(clock.millis()),
                    String.valueOf(window.toMillis()),
                    String.valueOf(limit),
                    ids.newId().toString());
            return Long.valueOf(RECORDED).equals(recorded);
        } catch (DataAccessException e) {
            throw new SecretStoreUnavailableException("Counting a secret issue failed: Valkey is unavailable", e);
        }
    }

    /** Writes hash, zero failures and expiry in one transaction, so a secret never exists without its TTL. */
    private static SessionCallback<List<Object>> replaceSecret(String redisKey, String hash, Duration ttl) {
        return new SessionCallback<>() {
            @Override
            @SuppressWarnings("unchecked")
            public <K, V> List<Object> execute(RedisOperations<K, V> operations) {
                var strings = (RedisOperations<String, String>) operations;
                strings.multi();
                strings.opsForHash().putAll(redisKey, Map.of(HASH_FIELD, hash, FAILURES_FIELD, "0"));
                strings.expire(redisKey, ttl);
                return strings.exec();
            }
        };
    }

    private static void requireSecret(String secret) {
        Objects.requireNonNull(secret, "secret");
        if (secret.isBlank()) {
            throw new IllegalArgumentException("secret must not be blank");
        }
    }

    private static void requirePositive(Duration duration, String name) {
        Objects.requireNonNull(duration, name);
        if (duration.isNegative() || duration.isZero()) {
            throw new IllegalArgumentException(name + " must be positive, was " + duration);
        }
    }

    private static RedisScript<Long> script(String name) {
        return RedisScript.of(new ClassPathResource(name, ValkeyShortLivedSecretStore.class), Long.class);
    }
}
