package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

class LegalVersionsTest {

    private static final LegalVersions CURRENT = new LegalVersions("2026-09", "2026-08");

    @Test
    void theCurrentVersionsAreAccepted() {
        assertThat(CURRENT.areCurrent("2026-09", "2026-08")).isTrue();
    }

    @ParameterizedTest
    @CsvSource({"2026-01, 2026-08", "2026-09, 2026-01", "'2026-09 ', 2026-08"})
    void anyOtherVersionIsOutdated(String terms, String privacy) {
        assertThat(CURRENT.areCurrent(terms, privacy)).isFalse();
    }
}
