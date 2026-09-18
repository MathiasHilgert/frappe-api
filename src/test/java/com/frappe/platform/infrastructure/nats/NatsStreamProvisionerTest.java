package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import io.nats.client.Connection;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamManagement;
import io.nats.client.api.Error;
import io.nats.client.support.Status;
import org.junit.jupiter.api.Test;

class NatsStreamProvisionerTest {

    @Test
    void provisioningFailureNamesTheServerError() throws Exception {
        // Given
        var connection = mock(Connection.class);
        var management = mock(JetStreamManagement.class);
        var error = new JetStreamApiException(Error.convert(new Status(500, "insufficient resources")));
        when(connection.jetStreamManagement()).thenReturn(management);
        when(management.getStreamInfo("FRAPPE")).thenThrow(error);

        // When / Then
        assertThatExceptionOfType(NatsProvisioningException.class)
                .isThrownBy(() -> new NatsStreamProvisioner().provision(connection))
                .withMessageContaining("FRAPPE")
                .withMessageContaining("insufficient resources")
                .withMessageNotContaining("-js")
                .withCause(error);
    }
}
