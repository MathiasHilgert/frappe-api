package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;

import org.junit.jupiter.api.Test;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.Externalized;

class NatsExternalizationRoutingTest {

    @Externalized
    record NotAnEnvelope(String value) {}

    record NotExternalized(String value) {}

    final EventExternalizationConfiguration configuration = new NatsConfiguration().eventExternalizationConfiguration();

    @Test
    void selectsOnlyExternalizedEvents() {
        assertThat(configuration.supports(new NotAnEnvelope("x"))).isTrue();
        assertThat(configuration.supports(new NotExternalized("x"))).isFalse();
    }

    @Test
    void explainsWhenAnExternalizedEventLacksTheEnvelope() {
        assertThatIllegalStateException()
                .isThrownBy(() -> configuration.determineTarget(new NotAnEnvelope("x")))
                .withMessageContaining(NotAnEnvelope.class.getName())
                .withMessageContaining("DomainEvent");
    }
}
