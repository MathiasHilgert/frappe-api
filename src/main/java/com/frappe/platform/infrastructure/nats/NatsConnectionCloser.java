package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
import java.time.Duration;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeoutException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Closes a NATS connection gracefully: drain first so buffered publishes reach the server, then close. */
final class NatsConnectionCloser {

    private static final Logger log = LoggerFactory.getLogger(NatsConnectionCloser.class);

    private NatsConnectionCloser() {}

    /**
     * Drains the connection, then closes it. An interrupt still closes the connection and keeps the interrupt flag, so
     * shutdown never leaks a socket and callers up the stack still see the interrupt.
     *
     * @param connection the connection to close
     * @param timeout how long draining may take
     */
    static void drainAndClose(Connection connection, Duration timeout) {
        try {
            connection.drain(timeout).get();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            close(connection);
        } catch (TimeoutException | ExecutionException | IllegalStateException e) {
            log.atWarn().setCause(e).log("NATS drain did not finish cleanly; closing");
            close(connection);
        }
    }

    private static void close(Connection connection) {
        try {
            connection.close();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
