package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class SecretKeyTest {

    private static final UUID SUBJECT = UUID.fromString("01996a4e-0000-7000-8000-000000000001");

    @Test
    void acceptsLowercaseKebabNamesAndAnId() {
        // When
        var key = new SecretKey("identity", "email-verification", SUBJECT);

        // Then
        assertThat(key.subjectId()).isEqualTo(SUBJECT);
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "Identity", "email verification", "ana@example.com", "-reset", "reset:code", "1st"})
    void rejectsNamesThatAreNotLowercaseKebabCase(String name) {
        assertThatIllegalArgumentException().isThrownBy(() -> new SecretKey(name, "reset", SUBJECT));
        assertThatIllegalArgumentException().isThrownBy(() -> new SecretKey("identity", name, SUBJECT));
    }

    @Test
    void buildsTheKeyFromATypedPurpose() {
        // When
        var key = SecretKey.of(IdentitySecrets.EMAIL_PROOF, SUBJECT);

        // Then
        assertThat(key).isEqualTo(new SecretKey("identity", "email-proof", SUBJECT));
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
        };

        // Then
        assertThatIllegalArgumentException().isThrownBy(() -> SecretKey.of(purpose, SUBJECT));
    }

    @Test
    void requiresASubjectId() {
        assertThatNullPointerException().isThrownBy(() -> new SecretKey("identity", "reset", null));
    }
}
