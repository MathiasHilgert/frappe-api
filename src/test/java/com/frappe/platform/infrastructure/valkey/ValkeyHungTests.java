package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.junit.jupiter.api.Assertions.assertTimeoutPreemptively;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.IdentityLimits;
import com.frappe.platform.IdentitySecrets;
import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.SecretKey;
import com.frappe.platform.SecretStoreUnavailableException;
import com.frappe.platform.ShortLivedSecretStore;
import com.redis.testcontainers.RedisContainer;
import java.time.Duration;
import org.assertj.core.api.ThrowableAssert.ThrowingCallable;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/**
 * A Valkey that accepts the connection but never answers (paused container, as in a network partition or a stalled
 * server): calls must give up after the configured timeout instead of hanging a login, and work again afterwards.
 */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestValkeyConfiguration.class})
@ActiveProfiles("local")
class ValkeyHungTests {

    private static final Duration BOUND = Duration.ofSeconds(3);

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    RateLimiter limiter;

    @Autowired
    RedisContainer valkey;

    @Autowired
    IdGenerator ids;

    @AfterEach
    void resume() {
        if (isPaused()) {
            valkey.getDockerClient()
                    .unpauseContainerCmd(valkey.getContainerId())
                    .exec();
        }
    }

    @Test
    void consumeAndTryConsumeFailAsUnavailableWithinTheTimeout() {
        // Given a working store, then a hung Valkey
        var secretKey = SecretKey.of(IdentitySecrets.EMAIL_PROOF, ids.newId());
        var limitKey = LimitKey.ofId(IdentityLimits.LOGIN_PER_ACCOUNT, ids.newId());
        secrets.put(secretKey, "493817", Duration.ofMinutes(10));
        valkey.getDockerClient().pauseContainerCmd(valkey.getContainerId()).exec();

        // Then
        assertUnavailableWithinTheBound(() -> secrets.consume(secretKey, "493817"));
        assertUnavailableWithinTheBound(() -> limiter.tryConsume(limitKey));

        // And once Valkey answers again, so does the store
        resume();
        assertThat(secrets.consume(secretKey, "493817")).isTrue();
        assertThat(limiter.tryConsume(limitKey)).isTrue();
    }

    private boolean isPaused() {
        return Boolean.TRUE.equals(valkey.getDockerClient()
                .inspectContainerCmd(valkey.getContainerId())
                .exec()
                .getState()
                .getPaused());
    }

    private static void assertUnavailableWithinTheBound(ThrowingCallable call) {
        assertTimeoutPreemptively(
                BOUND, () -> assertThatThrownBy(call).isInstanceOf(SecretStoreUnavailableException.class));
    }
}
