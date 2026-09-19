package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import java.net.InetAddress;
import java.time.Duration;
import java.util.regex.Pattern;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.core.ScanOptions;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;
import org.springframework.test.context.ActiveProfiles;

/** What a raw read of Valkey reveals: hashes and ids only, never a usable code or an email address. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestValkeyConfiguration.class})
@ActiveProfiles("local")
class ValkeyContentIntegrationTests {

    private static final Pattern KEY = Pattern.compile(
            "frappe:(secret|secret-issues|rate-limit):[a-z0-9-]+:[a-z0-9-]+:[0-9a-f.:/-]+(:\\d+-per-\\d+ms)?");

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    RateLimiter limiter;

    @Autowired
    StringRedisTemplate redis;

    @Autowired
    IdGenerator ids;

    @Test
    void storesOnlyTheArgon2HashOfASecret() {
        // Given
        var key = new SecretKey("identity", "email-verification", ids.newId());

        // When
        secrets.put(key, "493817", Duration.ofMinutes(10));

        // Then
        var stored = redis.<String, String>opsForHash().entries(ValkeyKeys.secret(key));
        assertThat(stored).containsOnlyKeys("hash", "failures");
        assertThat(stored.get("hash")).startsWith("$argon2id$").doesNotContain("493817");
        assertThat(stored.get("failures")).isEqualTo("0");
        assertThat(Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8().matches("493817", stored.get("hash")))
                .as("a dump without the pepper cannot be brute-forced")
                .isFalse();
        assertThat(redis.getExpire(ValkeyKeys.secret(key))).isPositive();
    }

    @Test
    void keysHoldIdsAndAddressesButNoEmailAddress() throws Exception {
        // Given everything the platform writes
        var account = ids.newId();
        var secretKey = new SecretKey("identity", "password-reset", account);
        secrets.put(secretKey, "493817", Duration.ofMinutes(10));
        secrets.countIssue(secretKey, Duration.ofHours(1), 5);
        limiter.tryConsume(LimitKey.ofId("identity", "login", account, 5, Duration.ofMinutes(1)));
        limiter.tryConsume(LimitKey.ofAddress(
                "identity", "login", InetAddress.getByName("2001:db8::7"), 20, Duration.ofMinutes(1)));

        // When
        var keys = redis.scan(ScanOptions.scanOptions().match("*").build()).stream()
                .toList();

        // Then
        assertThat(keys)
                .contains(
                        "frappe:secret:identity:password-reset:" + account,
                        "frappe:secret-issues:identity:password-reset:" + account,
                        "frappe:rate-limit:identity:login:" + account + ":5-per-60000ms",
                        "frappe:rate-limit:identity:login:2001:db8:0:0::/64:20-per-60000ms")
                .allMatch(key -> KEY.matcher(key).matches())
                .noneMatch(key -> key.contains("@"));
    }
}
