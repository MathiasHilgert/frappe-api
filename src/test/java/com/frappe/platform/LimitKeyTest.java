package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import java.net.InetAddress;
import java.time.Duration;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class LimitKeyTest {

    private static final UUID ACCOUNT = UUID.fromString("01996a4e-0000-7000-8000-000000000001");

    private static final Duration MINUTE = Duration.ofMinutes(1);

    @Test
    void limitsAnAccountById() {
        // When
        var key = LimitKey.ofId(IdentityLimits.LOGIN_PER_ACCOUNT, ACCOUNT);

        // Then the definition comes from the purpose
        assertThat(key)
                .isEqualTo(new LimitKey(
                        "identity", "login-per-account", "01996a4e-0000-7000-8000-000000000001", 3, MINUTE));
    }

    @Test
    void limitsAnIpv4AddressByItsCanonicalForm() throws Exception {
        // When
        var key = LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, InetAddress.getByName("203.0.113.7"));

        // Then
        assertThat(key.subject()).isEqualTo("203.0.113.7");
    }

    @Test
    void limitsAnIpv6AddressByItsSlash64Prefix() throws Exception {
        // Given two addresses of one /64, which a single client can rotate through freely
        var first = InetAddress.getByName("2001:DB8:1:2:0:0:0:7");
        var second = InetAddress.getByName("2001:db8:1:2:ffff:ffff:ffff:1");

        // When
        var firstKey = LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, first);
        var secondKey = LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, second);

        // Then
        assertThat(firstKey.subject()).isEqualTo("2001:db8:1:2::/64");
        assertThat(secondKey).isEqualTo(firstKey);
    }

    @Test
    void separatesIpv6AddressesOfDifferentSlash64Prefixes() throws Exception {
        // Given
        var first = InetAddress.getByName("2001:db8:1:2::7");
        var neighbour = InetAddress.getByName("2001:db8:1:3::7");

        // Then
        assertThat(LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, neighbour)
                        .subject())
                .isEqualTo("2001:db8:1:3::/64")
                .isNotEqualTo(LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, first)
                        .subject());
    }

    @ParameterizedTest
    @ValueSource(strings = {"01996a4e-0000-7000-8000-000000000001", "203.0.113.7", "2001:db8:1:2::/64", "0:0:0:0::/64"})
    void acceptsTheCanonicalFormsItBuilds(String subject) {
        assertThat(new LimitKey("identity", "login", subject, 5, MINUTE).subject())
                .isEqualTo(subject);
    }

    @ParameterizedTest
    @ValueSource(
            strings = {
                "",
                "ana@example.com",
                "Ana",
                "fe80::1%eth0",
                "a b",
                "5551234567",
                "555123456",
                "5551-234-567",
                "01996A4E-0000-7000-8000-000000000001",
                "1996a4e-0-7000-8000-1",
                "010.0.0.1",
                "203.0.113",
                "2001:db8::7",
                "2001:db8:0:0:0:0:0:7",
                "2001:0db8:1:2::/64",
                "2001:db8:1:2::/48"
            })
    void rejectsSubjectsThatAreNotACanonicalIdAddressOrPrefix(String subject) {
        assertThatIllegalArgumentException().isThrownBy(() -> new LimitKey("identity", "login", subject, 5, MINUTE));
    }

    @Test
    void rejectsNamesThatAreNotLowercaseKebabCase() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey("Identity", "login", ACCOUNT.toString(), 5, MINUTE));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey("identity", "log in", ACCOUNT.toString(), 5, MINUTE));
    }

    @Test
    void rejectsAPurposeWhoseDefinitionIsInvalid() {
        // Given
        var zeroCapacity = new LimitPurpose() {
            @Override
            public String module() {
                return "identity";
            }

            @Override
            public String name() {
                return "LOGIN";
            }

            @Override
            public long capacity() {
                return 0;
            }

            @Override
            public Duration period() {
                return MINUTE;
            }
        };

        // Then
        assertThatIllegalArgumentException().isThrownBy(() -> LimitKey.ofId(zeroCapacity, ACCOUNT));
    }

    @Test
    void requiresAPeriodOfWholeMilliseconds() {
        // Sub-millisecond parts would vanish from the Valkey key and let two definitions share one bucket
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey("identity", "login", ACCOUNT.toString(), 5, Duration.ofNanos(500_000)));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey(
                        "identity",
                        "login",
                        ACCOUNT.toString(),
                        5,
                        Duration.ofMillis(1).plusNanos(1)));
        assertThat(new LimitKey("identity", "login", ACCOUNT.toString(), 5, Duration.ofMillis(1)).period())
                .isEqualTo(Duration.ofMillis(1));
    }

    @Test
    void requiresAPositiveCapacityAndPeriod() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey("identity", "login", ACCOUNT.toString(), 0, MINUTE));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new LimitKey("identity", "login", ACCOUNT.toString(), 5, Duration.ZERO));
    }
}
