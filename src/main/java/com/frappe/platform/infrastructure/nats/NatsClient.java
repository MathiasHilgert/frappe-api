package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
import io.nats.client.ConnectionListener.Events;
import io.nats.client.Nats;
import io.nats.client.Options;
import java.io.IOException;
import java.time.Duration;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
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
    private static final int UNLIMITED_RECONNECTS = -1;

    private final NatsProperties properties;
    private final Consumer<Connection> onConnected;
    private final AtomicReference<Connection> connection = new AtomicReference<>();
    // One thread: setups for rapid reconnects run one after another, never concurrently.
    private final ExecutorService setup = Executors.newSingleThreadExecutor(
            Thread.ofVirtual().name("nats-setup").factory());
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
            throw new NatsUnavailableException("NATS at " + properties.url()
                    + " is not connected yet; the publication stays incomplete and is resubmitted once it connects");
        }
        return current;
    }

    @Override
    public void start() {
        running = true;
        if (connectAtStartup()) {
            return;
        }
        log.atWarn()
                .addKeyValue(LogFields.NATS_URL, properties.url())
                .log(
                        "NATS is unavailable; starting without it. Externalized events stay incomplete and are"
                                + " published once NATS is reachable (run 'docker compose up -d nats' or set FRAPPE_NATS_URL).");
        connector = Thread.ofVirtual().name("nats-connect").start(this::connectUntilReachable);
    }

    // An interrupt here must not leave the application silently without NATS: keep the flag, retry in the background.
    private boolean connectAtStartup() {
        try {
            return tryConnect();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            log.atWarn()
                    .addKeyValue(LogFields.NATS_URL, properties.url())
                    .log("Interrupted while connecting to NATS; retrying in the background");
            return false;
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

    boolean isRetrying() {
        var loop = connector;
        return loop != null && loop.isAlive();
    }

    @Override
    public void close() {
        running = false;
        var loop = connector;
        if (loop != null) {
            loop.interrupt();
        }
        setup.shutdownNow();
        var current = connection.getAndSet(null);
        if (current != null) {
            NatsConnectionCloser.drainAndClose(current, DRAIN_TIMEOUT);
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

    private boolean tryConnect() throws InterruptedException {
        try {
            var connected = Nats.connect(options());
            connection.compareAndSet(null, connected);
            // close() may have run meanwhile: only the side that removes the connection shuts it down.
            if (!running && connection.compareAndSet(connected, null)) {
                NatsConnectionCloser.drainAndClose(connected, DRAIN_TIMEOUT);
            }
            return true;
        } catch (IOException e) {
            log.atDebug()
                    .addKeyValue(LogFields.NATS_URL, properties.url())
                    .log("NATS not reachable: {}", e.getMessage());
            return false;
        }
    }

    private Options options() {
        return Options.builder()
                .server(properties.url())
                .connectionName(properties.connectionName())
                .connectionTimeout(properties.connectionTimeout())
                .reconnectWait(properties.reconnectWait())
                .maxReconnects(UNLIMITED_RECONNECTS)
                .connectionListener(this::onEvent)
                .build();
    }

    private void submitSetup(Connection source) {
        try {
            setup.execute(() -> onConnected.accept(source));
        } catch (RejectedExecutionException e) {
            log.debug("NATS client closed; skipping connect setup");
        }
    }

    void onEvent(Connection source, Events event) {
        switch (event) {
            case CONNECTED, RECONNECTED -> {
                connection.compareAndSet(null, source);
                log.atInfo()
                        .addKeyValue(LogFields.NATS_URL, properties.url())
                        .addKeyValue(LogFields.CONNECTION_EVENT, event.name())
                        .log("NATS {}", event.getEvent());
                submitSetup(source);
            }
            case DISCONNECTED ->
                log.atWarn()
                        .addKeyValue(LogFields.NATS_URL, properties.url())
                        .log("NATS disconnected; publications fail and stay incomplete until it reconnects");
            default -> log.debug("NATS {}", event.getEvent());
        }
    }
}
