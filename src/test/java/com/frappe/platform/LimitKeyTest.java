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
        var key = LimitKey.ofId("identity", "login", ACCOUNT, 5, MINUTE);

        // Then
        assertThat(key.subject()).isEqualTo("01996a4e-0000-7000-8000-000000000001");
        assertThat(key.capacity()).isEqualTo(5);
        assertThat(key.period()).isEqualTo(MINUTE);
    }

    @Test
    void limitsAnIpAddressByItsCanonicalForm() throws Exception {
        // Given
        var v4 = InetAddress.getByName("203.0.113.7");
        var v6 = InetAddress.getByName("2001:DB8:0:0:0:0:0:1");

        // Then
        assertThat(LimitKey.ofAddress("identity", "login", v4, 20, MINUTE).subject())
                .isEqualTo("203.0.113.7");
        assertThat(LimitKey.ofAddress("identity", "login", v6, 20, MINUTE).subject())
                .isEqualTo("2001:db8:0:0:0:0:0:1");
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "ana@example.com", "Ana", "fe80::1%eth0", "a b"})
    void rejectsSubjectsThatAreNeitherIdsNorAddresses(String subject) {
        assertThatIllegalArgumentException().isThrownBy(() -> new LimitKey("identity", "login", subject, 5, MINUTE));
    }

    @Test
    void rejectsNamesThatAreNotLowercaseKebabCase() {
        assertThatIllegalArgumentException().isThrownBy(() -> LimitKey.ofId("Identity", "login", ACCOUNT, 5, MINUTE));
        assertThatIllegalArgumentException().isThrownBy(() -> LimitKey.ofId("identity", "log in", ACCOUNT, 5, MINUTE));
    }

    @Test
    void requiresAPositiveCapacityAndPeriod() {
        assertThatIllegalArgumentException().isThrownBy(() -> LimitKey.ofId("identity", "login", ACCOUNT, 0, MINUTE));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> LimitKey.ofId("identity", "login", ACCOUNT, 5, Duration.ZERO));
    }
}
