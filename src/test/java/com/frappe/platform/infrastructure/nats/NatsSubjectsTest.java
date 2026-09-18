package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class NatsSubjectsTest {

    record PersonSessionOpened(
            UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Test
    void buildsSubjectFromModuleEventNameAndVersion() {
        // Given
        var event = new PersonSessionOpened(UUID.randomUUID(), Instant.EPOCH, UUID.randomUUID(), 1, 2);

        // When / Then
        assertThat(NatsSubjects.of(event)).isEqualTo("frappe.platform.person-session-opened.v2");
    }

    @Test
    void namesEventTypeAsModuleAndKebabName() {
        // When / Then
        assertThat(NatsSubjects.eventType(PersonSessionOpened.class)).isEqualTo("platform.person-session-opened");
    }

    @Test
    void rejectsEventsOutsideTheFrappeBasePackage() {
        // When / Then
        assertThatIllegalArgumentException()
                .isThrownBy(() -> NatsSubjects.eventType(String.class))
                .withMessageContaining("java.lang.String")
                .withMessageContaining("com.frappe.<module>");
    }
}
