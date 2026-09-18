package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import io.nats.client.Connection;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamManagement;
import io.nats.client.api.Error;
import io.nats.client.api.StreamConfiguration;
import io.nats.client.support.Status;
import java.time.Duration;
import org.junit.jupiter.api.Test;

class NatsClientTest {

    @Test
    void closesTheConnectionAndKeepsTheInterruptWhenDrainIsInterrupted() throws Exception {
        var connection = mock(Connection.class);
        when(connection.drain(any())).thenThrow(new InterruptedException());

        NatsClient.shutdown(connection, Duration.ofSeconds(1));

        verify(connection).close();
        assertThat(Thread.interrupted()).isTrue();
    }

    @Test
    void provisioningFailureNamesTheServerError() throws Exception {
        var connection = mock(Connection.class);
        var management = mock(JetStreamManagement.class);
        var error = new JetStreamApiException(Error.convert(new Status(500, "insufficient resources")));
        when(connection.jetStreamManagement()).thenReturn(management);
        when(management.getStreamInfo("FRAPPE")).thenThrow(error);

        assertThatIllegalStateException()
                .isThrownBy(() -> new NatsStreamProvisioner().provision(connection))
                .withMessageContaining("FRAPPE")
                .withMessageContaining("insufficient resources")
                .withMessageNotContaining("-js");
    }

    @Test
    void reportsANotYetConnectedClientClearly() {
        var client = new NatsClient(
                new NatsProperties(
                        "nats://localhost:1",
                        "test",
                        Duration.ofMillis(1),
                        Duration.ofMillis(1),
                        Duration.ofSeconds(1)),
                connection -> {});

        assertThatIllegalStateException()
                .isThrownBy(client::connection)
                .withMessageContaining("nats://localhost:1")
                .withMessageContaining("not connected");
    }

    static StreamConfiguration unused() {
        return null;
    }
}
