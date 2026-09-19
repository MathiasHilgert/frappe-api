package com.frappe.platform.infrastructure.nats;

import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;

import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import io.nats.client.Connection;
import org.junit.jupiter.api.Test;
import org.springframework.context.ApplicationEventPublisher;

class NatsConnectSetupTest {

    @Test
    void aConnectProvisionsTheStreamThenAnnouncesTheRecoveredTransportOnce() {
        // Given
        var provisioner = mock(NatsStreamProvisioner.class);
        var events = mock(ApplicationEventPublisher.class);
        var connection = mock(Connection.class);
        var setup = new NatsConnectSetup(provisioner, events);

        // When
        setup.accept(connection);

        // Then
        var order = inOrder(provisioner, events);
        order.verify(provisioner).provision(connection);
        order.verify(events).publishEvent(MessagingTransportRecovered.NATS);
        verify(events, times(1)).publishEvent(MessagingTransportRecovered.NATS);
    }
}
