package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import org.junit.jupiter.api.Test;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.Externalized;

class NatsExternalizationRoutingTest {

    @Externalized
    record NotAnEnvelope(String value) {}

    record NotExternalized(String value) {}

    @Externalized("custom.subject")
    record CustomSubject(
            java.util.UUID eventId,
            java.time.Instant occurredAt,
            java.util.UUID aggregateId,
            long aggregateVersion,
            int eventVersion)
            implements com.frappe.platform.DomainEvent {}

    final EventExternalizationConfiguration configuration = new NatsConfiguration().eventExternalizationConfiguration();

    @Test
    void selectsOnlyExternalizedEvents() {
        // When / Then
        assertThat(configuration.supports(new NotAnEnvelope("x"))).isTrue();
        assertThat(configuration.supports(new NotExternalized("x"))).isFalse();
    }

    @Test
    void explainsWhenAnExternalizedEventLacksTheEnvelope() {
        // When / Then
        assertThatExceptionOfType(InvalidExternalizedEventException.class)
                .isThrownBy(() -> configuration.determineTarget(new NotAnEnvelope("x")))
                .withMessageContaining(NotAnEnvelope.class.getName())
                .withMessageContaining("DomainEvent");
    }

    @Test
    void rejectsACustomTargetBecauseTheSubjectIsDerived() {
        // Given
        var event = new CustomSubject(null, null, null, 1, 1);

        // When / Then
        assertThatExceptionOfType(InvalidExternalizedEventException.class)
                .isThrownBy(() -> configuration.determineTarget(event))
                .withMessageContaining(CustomSubject.class.getName())
                .withMessageContaining("custom.subject")
                .withMessageContaining("frappe.platform.custom-subject.v1");
    }
}
