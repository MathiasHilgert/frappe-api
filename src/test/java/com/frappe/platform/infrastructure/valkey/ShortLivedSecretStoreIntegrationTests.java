package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import java.time.Duration;
import java.util.List;
import java.util.Properties;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.core.RedisCallback;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.test.context.ActiveProfiles;

/** The secret store's contract against a real Valkey 9. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestValkeyConfiguration.class})
@ActiveProfiles("local")
class ShortLivedSecretStoreIntegrationTests {

    private static final Duration TEN_MINUTES = Duration.ofMinutes(10);

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    IdGenerator ids;

    @Autowired
    StringRedisTemplate redis;

    @Test
    void consumesTheRightSecretOnce() {
        // Given
        var key = newKey();
        secrets.put(key, "493817", TEN_MINUTES);

        // When
        var first = secrets.consume(key, "493817");
        var second = secrets.consume(key, "493817");

        // Then
        assertThat(first).isTrue();
        assertThat(second).isFalse();
    }

    @Test
    void twoConcurrentRightSubmissionsGiveExactlyOneSuccess() throws Exception {
        // Given
        var key = newKey();
        secrets.put(key, "493817", TEN_MINUTES);
        var start = new CountDownLatch(1);
        Callable<Boolean> submission = () -> {
            start.await();
            return secrets.consume(key, "493817");
        };

        // When
        List<Boolean> outcomes;
        try (var executor = Executors.newFixedThreadPool(2)) {
            var futures = List.of(executor.submit(submission), executor.submit(submission));
            start.countDown();
            outcomes = futures.stream()
                    .map(ShortLivedSecretStoreIntegrationTests::join)
                    .toList();
        }

        // Then
        assertThat(outcomes).containsExactlyInAnyOrder(true, false);
    }

    @Test
    void theRightSecretStillWorksAfterFourWrongAttempts() {
        // Given
        var key = newKey();
        secrets.put(key, "493817", TEN_MINUTES);
        for (var attempt = 0; attempt < 4; attempt++) {
            assertThat(secrets.consume(key, "000000")).isFalse();
        }

        // When
        var consumed = secrets.consume(key, "493817");

        // Then
        assertThat(consumed).isTrue();
    }

    @Test
    void theFifthWrongAttemptDeletesTheSecret() {
        // Given
        var key = newKey();
        secrets.put(key, "493817", TEN_MINUTES);
        for (var attempt = 0; attempt < 5; attempt++) {
            assertThat(secrets.consume(key, "000000")).isFalse();
        }

        // When
        var consumed = secrets.consume(key, "493817");

        // Then
        assertThat(consumed).isFalse();
    }

    @Test
    void aSecondPutReplacesTheSecretWithAFreshFailureCount() {
        // Given four failures against the first secret
        var key = newKey();
        secrets.put(key, "111111", TEN_MINUTES);
        for (var attempt = 0; attempt < 4; attempt++) {
            secrets.consume(key, "000000");
        }

        // When
        secrets.put(key, "222222", TEN_MINUTES);

        // Then the old secret is dead, and the new one survives four more failures
        assertThat(secrets.consume(key, "111111")).isFalse();
        for (var attempt = 0; attempt < 3; attempt++) {
            assertThat(secrets.consume(key, "000000")).isFalse();
        }
        assertThat(secrets.consume(key, "222222")).isTrue();
    }

    @Test
    void consumeIsFalseOnceTheTtlHasPassed() {
        // Given
        var key = newKey();
        secrets.put(key, "493817", Duration.ofMillis(200));

        // When Valkey's own clock passes the TTL
        await().pollDelay(Duration.ofMillis(400)).atMost(Duration.ofSeconds(1)).until(() -> true);

        // Then
        assertThat(secrets.consume(key, "493817")).isFalse();
    }

    @Test
    void consumeIsFalseWhenNoSecretWasPut() {
        assertThat(secrets.consume(newKey(), "493817")).isFalse();
    }

    @Test
    void putsOnTheSharedConnectionWithoutOpeningNewOnes() {
        // Given
        var key = newKey();
        secrets.put(key, "111111", TEN_MINUTES);
        var before = connectionsReceived();

        // When
        for (var put = 0; put < 5; put++) {
            secrets.put(key, "22222" + put, TEN_MINUTES);
        }

        // Then
        assertThat(connectionsReceived()).isEqualTo(before);
    }

    private long connectionsReceived() {
        var stats = redis.execute((RedisCallback<Properties>)
                connection -> connection.serverCommands().info("stats"));
        return Long.parseLong(stats.getProperty("total_connections_received"));
    }

    private SecretKey newKey() {
        return new SecretKey("identity", "email-verification", ids.newId());
    }

    private static boolean join(Future<Boolean> future) {
        try {
            return future.get();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }
}
