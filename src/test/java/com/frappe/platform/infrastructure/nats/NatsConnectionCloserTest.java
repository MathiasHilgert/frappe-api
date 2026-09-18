package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import io.nats.client.Connection;
import java.time.Duration;
import org.junit.jupiter.api.Test;

class NatsConnectionCloserTest {

    @Test
    void closesTheConnectionAndKeepsTheInterruptWhenDrainIsInterrupted() throws Exception {
        // Given
        var connection = mock(Connection.class);
        when(connection.drain(any())).thenThrow(new InterruptedException());

        // When
        NatsConnectionCloser.drainAndClose(connection, Duration.ofSeconds(1));

        // Then
        verify(connection).close();
        assertThat(Thread.interrupted()).isTrue();
    }
}
