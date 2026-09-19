package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.SecretKey;
import java.time.Duration;
import java.time.Instant;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;
import org.springframework.test.context.ActiveProfiles;

/** The sliding-window issue cap against a real Valkey 9, with a clock the test moves. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestValkeyConfiguration.class})
@ActiveProfiles("local")
class SecretIssueCapIntegrationTests {

    private static final Duration HOUR = Duration.ofHours(1);

    private static final int FIVE = 5;

    private final MutableClock clock = new MutableClock(Instant.parse("2026-09-19T12:00:00Z"));

    @Autowired
    StringRedisTemplate redis;

    @Autowired
    IdGenerator ids;

    @Test
    void refusesTheSixthIssueWithinTheWindow() {
        // Given five issues spread over the hour
        var store = store();
        var key = newKey();
        IntStream.range(0, FIVE).forEach(issue -> {
            assertThat(store.countIssue(key, HOUR, FIVE)).isTrue();
            clock.advance(Duration.ofMinutes(10));
        });

        // When
        var sixth = store.countIssue(key, HOUR, FIVE);

        // Then
        assertThat(sixth).isFalse();
    }

    @Test
    void allowsIssuingAgainOnceTheOldestIssueLeavesTheWindow() {
        // Given five issues, the oldest at the start and the others ten minutes later
        var store = store();
        var key = newKey();
        var start = clock.instant();
        assertThat(store.countIssue(key, HOUR, FIVE)).isTrue();
        clock.advance(Duration.ofMinutes(10));
        IntStream.range(1, FIVE)
                .forEach(issue -> assertThat(store.countIssue(key, HOUR, FIVE)).isTrue());

        // When / Then: refused while the oldest is inside the window, allowed once it left
        clock.advance(Duration.between(clock.instant(), start.plus(HOUR).minusMillis(1)));
        assertThat(store.countIssue(key, HOUR, FIVE)).isFalse();
        clock.advance(Duration.ofMillis(1));
        assertThat(store.countIssue(key, HOUR, FIVE)).isTrue();
        assertThat(store.countIssue(key, HOUR, FIVE)).isFalse();
    }

    @Test
    void capsEachKeySeparately() {
        // Given a key at its cap
        var store = store();
        var capped = newKey();
        IntStream.range(0, FIVE).forEach(issue -> store.countIssue(capped, HOUR, FIVE));

        // When
        var other = store.countIssue(newKey(), HOUR, FIVE);

        // Then
        assertThat(store.countIssue(capped, HOUR, FIVE)).isFalse();
        assertThat(other).isTrue();
    }

    private ValkeyShortLivedSecretStore store() {
        return new ValkeyShortLivedSecretStore(
                redis, Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8(), clock, ids);
    }

    private SecretKey newKey() {
        return new SecretKey("identity", "email-verification", ids.newId());
    }
}
