package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.SecretStoreUnavailableException;
import io.github.bucket4j.BucketConfiguration;
import io.github.bucket4j.TimeMeter;
import io.github.bucket4j.distributed.ExpirationAfterWriteStrategy;
import io.github.bucket4j.redis.lettuce.Bucket4jLettuce;
import io.github.bucket4j.redis.lettuce.cas.LettuceBasedProxyManager;
import io.lettuce.core.RedisException;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.util.concurrent.TimeUnit;
import org.springframework.dao.DataAccessException;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;

/**
 * {@link RateLimiter} on Bucket4j token buckets stored in Valkey. Bucket state is updated with compare-and-swap scripts,
 * so every instance sees and drains the same bucket. A bucket expires shortly after it is full again, when it holds no
 * information.
 */
final class ValkeyRateLimiter implements RateLimiter {

    /** How long a refilled bucket is kept, so a steady caller reuses it instead of recreating it on every call. */
    private static final Duration KEEP_AFTER_REFILL = Duration.ofSeconds(10);

    private final LettuceBasedProxyManager<byte[]> buckets;

    /**
     * Creates the rate limiter.
     *
     * @param connections Spring's Lettuce connection factory, whose shared connection carries the bucket commands
     * @param clock the application clock, for refills
     */
    ValkeyRateLimiter(LettuceConnectionFactory connections, Clock clock) {
        this.buckets = new Bucket4jLettuce.LettuceBasedProxyManagerBuilder<>(
                        new SharedConnectionRedisApi<byte[]>(connections))
                .expirationAfterWrite(
                        ExpirationAfterWriteStrategy.basedOnTimeForRefillingBucketUpToMax(KEEP_AFTER_REFILL))
                .clientClock(timeMeter(clock))
                .build();
    }

    @Override
    public boolean tryConsume(LimitKey key) {
        var redisKey = ValkeyKeys.rateLimit(key).getBytes(StandardCharsets.UTF_8);
        try {
            return buckets.builder().build(redisKey, () -> definition(key)).tryConsume(1);
        } catch (DataAccessException | RedisException e) {
            throw new SecretStoreUnavailableException("Checking a rate limit failed: Valkey is unavailable", e);
        }
    }

    private static BucketConfiguration definition(LimitKey key) {
        return BucketConfiguration.builder()
                .addLimit(limit -> limit.capacity(key.capacity()).refillGreedy(key.capacity(), key.period()))
                .build();
    }

    /** Bucket4j reads time through its own interface; this one reads the application clock. */
    private static TimeMeter timeMeter(Clock clock) {
        return new TimeMeter() {
            @Override
            public long currentTimeNanos() {
                var now = clock.instant();
                return TimeUnit.SECONDS.toNanos(now.getEpochSecond()) + now.getNano();
            }

            @Override
            public boolean isWallClockBased() {
                return true;
            }
        };
    }
}
