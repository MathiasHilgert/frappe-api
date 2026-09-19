package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.KeyedDigests;
import java.util.ArrayDeque;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class RecoveryCodesTest {

    private static final PersonId PERSON = new PersonId(UUID.fromString("01925f6e-0000-7000-8000-000000000001"));

    // Digests the value readably, so tests can see what was digested.
    private static final KeyedDigests DIGESTS = new KeyedDigests() {
        @Override
        public UUID subjectOf(String namespace, String value) {
            throw new UnsupportedOperationException();
        }

        @Override
        public String digestOf(String namespace, String value) {
            return namespace + "|" + value;
        }
    };

    @Test
    void eightDistinctCodesAreIssued() {
        // When
        var issued = RecoveryCodes.issue(PERSON, secrets("A", "B", "C", "D", "E", "F", "G", "H"), DIGESTS);

        // Then
        assertThat(issued.codes()).containsExactly("A", "B", "C", "D", "E", "F", "G", "H");
    }

    @Test
    void aRepeatedCodeIsDrawnAgain() {
        // When
        var issued = RecoveryCodes.issue(PERSON, secrets("A", "A", "B", "C", "D", "E", "F", "G", "H"), DIGESTS);

        // Then
        assertThat(issued.codes()).containsExactly("A", "B", "C", "D", "E", "F", "G", "H");
    }

    @Test
    void eachCodeIsStoredAsAKeyedDigestBoundToThePerson() {
        // When
        var issued = RecoveryCodes.issue(PERSON, secrets("A", "B", "C", "D", "E", "F", "G", "H"), DIGESTS);

        // Then
        assertThat(issued.digests())
                .hasSize(8)
                .first()
                .isEqualTo("identity.recovery-code|" + PERSON.value() + ":A");
    }

    private static Secrets secrets(String... codes) {
        var queue = new ArrayDeque<>(List.of(codes));
        return new Secrets() {
            @Override
            public String token() {
                throw new UnsupportedOperationException();
            }

            @Override
            public String code() {
                throw new UnsupportedOperationException();
            }

            @Override
            public String recoveryCode() {
                return queue.remove();
            }
        };
    }
}
