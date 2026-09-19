package com.frappe.platform.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import org.junit.jupiter.api.Test;

class RequestRefusedExceptionTest {

    enum TabError {
        ALREADY_CLOSED
    }

    @Test
    void carriesTheFailureAndNamesOnlyItsType() {
        // When
        var refused = new RequestRefusedException(TabError.ALREADY_CLOSED);

        // Then the failure's values (possibly personal data) stay out of the message
        assertThat(refused.failure()).isEqualTo(TabError.ALREADY_CLOSED);
        assertThat(refused.getMessage()).contains(TabError.class.getName()).doesNotContain("ALREADY_CLOSED");
    }

    @Test
    void needsAFailure() {
        assertThatNullPointerException().isThrownBy(() -> new RequestRefusedException(null));
    }
}
