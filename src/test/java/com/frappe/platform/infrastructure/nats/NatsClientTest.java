package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import io.nats.client.Connection;
import io.nats.client.ConnectionListener.Events;
import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
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
    void reportsANotYetConnectedClientClearly() {
        var client = new NatsClient(
                new NatsProperties(
                        "nats://localhost:1",
                        "test",
                        Duration.ofMillis(1),
                        Duration.ofMillis(1),
                        Duration.ofSeconds(1)),
                connection -> {});

        assertThatExceptionOfType(NatsUnavailableException.class)
                .isThrownBy(client::connection)
                .withMessageContaining("nats://localhost:1")
                .withMessageContaining("not connected");
    }

    @Test
    void drainsTheConnectionOnceEvenWhenClosedTwice() throws Exception {
        var connection = mock(Connection.class);
        when(connection.drain(any())).thenReturn(CompletableFuture.completedFuture(true));
        var client = client(it -> {});
        client.onEvent(connection, Events.CONNECTED);

        client.close();
        client.close();

        verify(connection, times(1)).drain(any());
    }

    @Test
    void runsConnectSetupOneAtATime() throws Exception {
        var running = new AtomicInteger();
        var overlapped = new AtomicBoolean();
        var done = new CountDownLatch(3);
        var client = client(it -> {
            if (running.incrementAndGet() > 1) {
                overlapped.set(true);
            }
            sleep();
            running.decrementAndGet();
            done.countDown();
        });
        var connection = mock(Connection.class);

        client.onEvent(connection, Events.CONNECTED);
        client.onEvent(connection, Events.RECONNECTED);
        client.onEvent(connection, Events.RECONNECTED);

        assertThat(done.await(5, TimeUnit.SECONDS)).isTrue();
        assertThat(overlapped).isFalse();
    }

    @Test
    void keepsRetryingAndTheInterruptWhenStartupIsInterrupted() {
        var client = client(it -> {});

        Thread.currentThread().interrupt();
        client.start();

        assertThat(Thread.interrupted()).isTrue();
        assertThat(client.isRunning()).isTrue();
        assertThat(client.isRetrying()).isTrue();
        client.close();
    }

    private static NatsClient client(java.util.function.Consumer<Connection> onConnected) {
        return new NatsClient(
                new NatsProperties(
                        "nats://localhost:1",
                        "test",
                        Duration.ofMillis(100),
                        Duration.ofMillis(50),
                        Duration.ofSeconds(1)),
                onConnected);
    }

    private static void sleep() {
        try {
            Thread.sleep(50);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
