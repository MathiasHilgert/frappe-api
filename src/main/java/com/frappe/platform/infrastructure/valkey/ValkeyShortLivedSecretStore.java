package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.SecretKey;
import com.frappe.platform.SecretStoreUnavailableException;
import com.frappe.platform.ShortLivedSecretStore;
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
 */
final class ValkeyShortLivedSecretStore implements ShortLivedSecretStore {

    private static final String HASH_FIELD = "hash";

    private static final String FAILURES_FIELD = "failures";

    private static final String MATCHED = "1";

    private static final String NOT_MATCHED = "0";

    private static final RedisScript<Long> CONSUME =
            RedisScript.of(new ClassPathResource("consume-secret.lua", ValkeyShortLivedSecretStore.class), Long.class);

    private final StringRedisTemplate redis;

    private final PasswordEncoder hashes;

    /**
     * Creates the store.
     *
     * @param redis the Valkey client
     * @param hashes the one-way encoder for stored secrets (Argon2)
     */
    ValkeyShortLivedSecretStore(StringRedisTemplate redis, PasswordEncoder hashes) {
        this.redis = redis;
        this.hashes = hashes;
    }

    @Override
    public void put(SecretKey key, String secret, Duration ttl) {
        Objects.requireNonNull(key, "key");
        requireSecret(secret);
        requirePositive(ttl);
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
            return Long.valueOf(1).equals(consumed);
        } catch (DataAccessException e) {
            throw new SecretStoreUnavailableException("Checking a secret failed: Valkey is unavailable", e);
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

    private static void requirePositive(Duration ttl) {
        Objects.requireNonNull(ttl, "ttl");
        if (ttl.isNegative() || ttl.isZero()) {
            throw new IllegalArgumentException("ttl must be positive, was " + ttl);
        }
    }
}
