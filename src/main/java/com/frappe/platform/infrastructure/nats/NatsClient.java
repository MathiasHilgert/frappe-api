package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
import io.nats.client.ConnectionListener.Events;
import io.nats.client.Nats;
import io.nats.client.Options;
import java.io.IOException;
import java.time.Duration;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;

/**
 * Owns the single NATS connection. NATS is optional at startup: if it is unreachable the application still starts and
 * a background loop keeps trying; once connected, jnats reconnects forever. Every (re)connect runs {@code onConnected}
 * (stream provisioning, resubmission of failed publications). Closing drains in-flight messages first.
 */
class NatsClient implements SmartLifecycle, AutoCloseable {

    private static final Logger log = LoggerFactory.getLogger(NatsClient.class);
    private static final Duration DRAIN_TIMEOUT = Duration.ofSeconds(10);

    private final NatsProperties properties;
    private final Consumer<Connection> onConnected;
    private final AtomicReference<Connection> connection = new AtomicReference<>();
    private volatile boolean running;
    private volatile Thread connector;

    NatsClient(NatsProperties properties, Consumer<Connection> onConnected) {
        this.properties = properties;
        this.onConnected = onConnected;
    }

    /** The live connection; fails with a clear message while NATS has not been reached yet. */
    Connection connection() {
        var current = connection.get();
        if (current == null) {
            throw new IllegalStateException("NATS at " + properties.url()
                    + " is not connected yet; the publication stays incomplete and is resubmitted once it connects");
        }
        return current;
    }

    @Override
    public void start() {
        running = true;
        if (!tryConnect()) {
            log.warn(
                    "NATS is unavailable at {}; starting without it. Externalized events stay incomplete and are"
                            + " published once NATS is reachable (run 'docker compose up -d nats' or set FRAPPE_NATS_URL).",
                    properties.url());
            connector = Thread.ofVirtual().name("nats-connect").start(this::connectUntilReachable);
        }
    }

    @Override
    public void stop() {
        // The connection closes in close(), after the beans that publish through it are destroyed.
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    @Override
    public void close() {
        running = false;
        var loop = connector;
        if (loop != null) {
            loop.interrupt();
        }
        var current = connection.get();
        if (current != null) {
            shutdown(current, DRAIN_TIMEOUT);
        }
    }

    /** Drains, then closes; an interrupt still closes the connection and keeps the interrupt flag. */
    static void shutdown(Connection connection, Duration timeout) {
        try {
            connection.drain(timeout).get();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            closeQuietly(connection);
        } catch (TimeoutException | ExecutionException | IllegalStateException e) {
            log.warn("NATS drain did not finish cleanly; closing: {}", e.toString());
            closeQuietly(connection);
        }
    }

    private static void closeQuietly(Connection connection) {
        try {
            connection.close();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private void connectUntilReachable() {
        try {
            while (running && !tryConnect()) {
                Thread.sleep(properties.reconnectWait());
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private boolean tryConnect() {
        try {
            var connected = Nats.connect(options());
            connection.set(connected);
            if (!running) {
                shutdown(connected, DRAIN_TIMEOUT);
            }
            return true;
        } catch (IOException e) {
            log.debug("NATS at {} not reachable: {}", properties.url(), e.getMessage());
            return false;
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return true;
        }
    }

    private Options options() {
        return Options.builder()
                .server(properties.url())
                .connectionName(properties.connectionName())
                .connectionTimeout(properties.connectionTimeout())
                .reconnectWait(properties.reconnectWait())
                .maxReconnects(-1)
                .connectionListener(this::onEvent)
                .build();
    }

    private void onEvent(Connection source, Events event) {
        switch (event) {
            case CONNECTED, RECONNECTED -> {
                connection.compareAndSet(null, source);
                log.info("NATS {} at {}", event.getEvent(), properties.url());
                Thread.ofVirtual().name("nats-on-connect").start(() -> onConnected.accept(source));
            }
            case DISCONNECTED ->
                log.warn(
                        "NATS disconnected from {}; publications fail and stay incomplete until it reconnects",
                        properties.url());
            default -> log.debug("NATS {}", event.getEvent());
        }
    }
}
