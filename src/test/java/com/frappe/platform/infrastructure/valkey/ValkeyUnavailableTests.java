package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.junit.jupiter.api.Assertions.assertTimeoutPreemptively;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.IdentityLimits;
import com.frappe.platform.IdentitySecrets;
import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.SecretKey;
import com.frappe.platform.SecretStoreUnavailableException;
import com.frappe.platform.ShortLivedSecretStore;
import java.time.Duration;
import org.assertj.core.api.ThrowableAssert.ThrowingCallable;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;

/**
 * Valkey holds only codes and rate limits: without it the API starts, everything else keeps serving and stays healthy,
 * and the Valkey ports fail fast with {@link SecretStoreUnavailableException} instead of hanging a login.
 */
@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "spring.data.redis.url=redis://localhost:1",
            "spring.data.redis.timeout=500ms",
            "spring.data.redis.connect-timeout=500ms"
        })
@AutoConfigureRestTestClient
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class ValkeyUnavailableTests {

    private static final Duration BOUND = Duration.ofSeconds(5);

    @Autowired
    RestTestClient http;

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    RateLimiter limiter;

    @Autowired
    IdGenerator ids;

    @Test
    void startsAndStaysHealthyWithoutValkey() {
        // When / Then
        http.get().uri("/actuator/health").exchange().expectStatus().isOk();
    }

    @Test
    void theSecretStoreFailsFastAsUnavailable() {
        // Given
        var key = SecretKey.of(IdentitySecrets.EMAIL_PROOF, ids.newId());

        // Then
        assertUnavailable(() -> secrets.put(key, "493817", Duration.ofMinutes(10)));
        assertUnavailable(() -> secrets.consume(key, "493817"));
        assertUnavailable(() -> secrets.countIssue(key, Duration.ofHours(1), 5));
    }

    @Test
    void theRateLimiterFailsFastAsUnavailable() {
        // Given
        var key = LimitKey.ofId(IdentityLimits.LOGIN_PER_ACCOUNT, ids.newId());

        // Then
        assertUnavailable(() -> limiter.tryConsume(key));
    }

    private static void assertUnavailable(ThrowingCallable call) {
        assertTimeoutPreemptively(
                BOUND,
                () -> assertThatThrownBy(call)
                        .isInstanceOf(SecretStoreUnavailableException.class)
                        .hasMessageContaining("Valkey is unavailable")
                        .hasCauseInstanceOf(RuntimeException.class));
    }
}
