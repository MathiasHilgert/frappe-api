package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.identity.domain.PasswordRejected.Reason;
import com.frappe.platform.Result;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

class PasswordPolicyTest {

    private static final String RAW = "correct horse battery staple";

    @Test
    void aBreachedPasswordIsRejected() {
        // Given
        var policy = new PasswordPolicy(password -> BreachStatus.BREACHED);

        // When
        var result = policy.check(RAW);

        // Then
        assertThat(result).isEqualTo(Result.failure(new PasswordRejected(Reason.BREACHED)));
    }

    @ParameterizedTest
    @EnumSource(
            value = BreachStatus.class,
            names = {"NOT_FOUND", "UNKNOWN"})
    void aPasswordNotKnownAsBreachedIsAcceptedEvenWhenTheCheckFailed(BreachStatus status) {
        // Given
        var policy = new PasswordPolicy(password -> status);

        // When
        var result = policy.check(RAW);

        // Then
        assertThat(result).isEqualTo(Password.of(RAW));
    }

    @Test
    void lengthIsCheckedBeforeTheBreachCheck() {
        // Given
        List<Password> checked = new ArrayList<>();
        var policy = new PasswordPolicy(password -> {
            checked.add(password);
            return BreachStatus.BREACHED;
        });

        // When
        var result = policy.check("short");

        // Then
        assertThat(result).isEqualTo(Result.failure(new PasswordRejected(Reason.TOO_SHORT)));
        assertThat(checked).isEmpty();
    }
}
