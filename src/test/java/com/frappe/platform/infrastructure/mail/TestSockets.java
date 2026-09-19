package com.frappe.platform.infrastructure.mail;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.net.ServerSocket;

/** Ports for tests that need a mail server to be unreachable. */
final class TestSockets {

    private TestSockets() {}

    /**
     * A local port nothing listens on (free at the time of the call).
     *
     * @return the port
     */
    static int closedPort() {
        try (var socket = new ServerSocket(0)) {
            return socket.getLocalPort();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}
