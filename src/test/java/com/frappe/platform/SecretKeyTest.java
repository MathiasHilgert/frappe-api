package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import java.time.Duration;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class SecretKeyTest {

    private static final UUID SUBJECT = UUID.fromString("01996a4e-0000-7000-8000-000000000001");

    private static final Duration TTL = Duration.ofMinutes(15);

    private static final Duration HOUR = Duration.ofHours(1);

    @Test
    void acceptsLowercaseKebabNamesAndAnId() {
        // When
        var key = new SecretKey("identity", "email-verification", SUBJECT, TTL, HOUR, 5);

        // Then
        assertThat(key.subjectId()).isEqualTo(SUBJECT);
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "Identity", "email verification", "ana@example.com", "-reset", "reset:code", "1st"})
    void rejectsNamesThatAreNotLowercaseKebabCase(String name) {
        assertThatIllegalArgumentException().isThrownBy(() -> new SecretKey(name, "reset", SUBJECT, TTL, HOUR, 5));
        assertThatIllegalArgumentException().isThrownBy(() -> new SecretKey("identity", name, SUBJECT, TTL, HOUR, 5));
    }

    @Test
    void buildsTheKeyFromATypedPurpose() {
        // When
        var key = SecretKey.of(IdentitySecrets.EMAIL_PROOF, SUBJECT);

        // Then the names and the definition come from the purpose
        assertThat(key).isEqualTo(new SecretKey("identity", "email-proof", SUBJECT, TTL, HOUR, 5));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "emailProof", "EMAIL-PROOF", "EMAIL__PROOF", "_EMAIL", "EMAIL PROOF"})
    void rejectsPurposeNamesThatAreNotUpperSnakeCase(String name) {
        // Given
        var purpose = new SecretPurpose() {
            @Override
            public String module() {
                return "identity";
            }

            @Override
            public String name() {
                return name;
            }

            @Override
            public Duration ttl() {
                return TTL;
            }

            @Override
            public Duration issueWindow() {
                return HOUR;
            }

            @Override
            public int issueLimit() {
                return 5;
            }
        };

        // Then
        assertThatIllegalArgumentException().isThrownBy(() -> SecretKey.of(purpose, SUBJECT));
    }

    @Test
    void requiresASubjectId() {
        assertThatNullPointerException().isThrownBy(() -> new SecretKey("identity", "reset", null, TTL, HOUR, 5));
    }

    @Test
    void rejectsATtlOrIssueWindowBelowOneMillisecond() {
        // A sub-millisecond duration would become PEXPIRE 0, which deletes the key at once
        var subMillisecond = Duration.ofNanos(999_999);

        assertThatIllegalArgumentException()
                .isThrownBy(() -> new SecretKey("identity", "reset", SUBJECT, subMillisecond, HOUR, 5));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new SecretKey("identity", "reset", SUBJECT, TTL, subMillisecond, 5));
    }

    @Test
    void requiresAPositiveIssueLimit() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new SecretKey("identity", "reset", SUBJECT, TTL, HOUR, 0));
    }
}
