package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.redis.testcontainers.RedisContainer;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.connection.RedisStandaloneConfiguration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
import org.springframework.test.context.ActiveProfiles;

/** Token buckets against a real Valkey 9. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestValkeyConfiguration.class})
@ActiveProfiles("local")
class RateLimiterIntegrationTests {

    private static final int N = 3;

    private static final Duration MINUTE = Duration.ofMinutes(1);

    @Autowired
    RateLimiter limiter;

    @Autowired
    LettuceConnectionFactory connections;

    @Autowired
    RedisContainer valkey;

    @Autowired
    IdGenerator ids;

    @Test
    void refusesTheCallAfterNCallsInThePeriod() {
        // Given
        var key = newKey();
        IntStream.range(0, N)
                .forEach(call -> assertThat(limiter.tryConsume(key)).isTrue());

        // When
        var nPlusOne = limiter.tryConsume(key);

        // Then
        assertThat(nPlusOne).isFalse();
    }

    @Test
    void allowsCallsAgainOnceThePeriodRefilledTheBucket() {
        // Given an empty bucket
        var clock = new MutableClock(Instant.parse("2026-09-19T12:00:00Z"));
        var rateLimiter = new ValkeyRateLimiter(connections, clock);
        var key = newKey();
        IntStream.range(0, N).forEach(call -> rateLimiter.tryConsume(key));
        assertThat(rateLimiter.tryConsume(key)).isFalse();

        // When
        clock.advance(MINUTE);

        // Then
        IntStream.range(0, N)
                .forEach(call -> assertThat(rateLimiter.tryConsume(key)).isTrue());
        assertThat(rateLimiter.tryConsume(key)).isFalse();
    }

    @Test
    void twoApplicationInstancesShareTheBucket() {
        // Given a second instance with its own Valkey connection
        var otherConnections = new LettuceConnectionFactory(
                new RedisStandaloneConfiguration(valkey.getRedisHost(), valkey.getRedisPort()));
        otherConnections.afterPropertiesSet();
        otherConnections.start();
        try {
            var otherInstance = new ValkeyRateLimiter(otherConnections, Clock.systemUTC());
            var key = newKey();

            // When the calls are spread over both instances
            assertThat(limiter.tryConsume(key)).isTrue();
            assertThat(otherInstance.tryConsume(key)).isTrue();
            assertThat(limiter.tryConsume(key)).isTrue();

            // Then both see the empty bucket
            assertThat(otherInstance.tryConsume(key)).isFalse();
            assertThat(limiter.tryConsume(key)).isFalse();
        } finally {
            otherConnections.destroy();
        }
    }

    @Test
    void limitsEachKeySeparately() {
        // Given
        var exhausted = newKey();
        IntStream.range(0, N).forEach(call -> limiter.tryConsume(exhausted));

        // Then
        assertThat(limiter.tryConsume(exhausted)).isFalse();
        assertThat(limiter.tryConsume(newKey())).isTrue();
    }

    @Test
    void aChangedDefinitionAppliesAtOnce() {
        // Given a subject that used 3 of 10 calls
        var account = ids.newId();
        IntStream.range(0, 3)
                .forEach(call -> limiter.tryConsume(LimitKey.ofId("identity", "login", account, 10, MINUTE)));

        // When the limit is tightened to 2 per minute (a deployment reacting to an attack)
        var tightened = LimitKey.ofId("identity", "login", account, 2, MINUTE);

        // Then the new definition holds, not the 7 tokens left in the old bucket
        assertThat(limiter.tryConsume(tightened)).isTrue();
        assertThat(limiter.tryConsume(tightened)).isTrue();
        assertThat(limiter.tryConsume(tightened)).isFalse();
    }

    private LimitKey newKey() {
        return LimitKey.ofId("identity", "login", ids.newId(), N, MINUTE);
    }
}
