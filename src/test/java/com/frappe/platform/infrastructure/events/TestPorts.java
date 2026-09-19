package com.frappe.platform.infrastructure.events;

import java.io.IOException;
import java.net.ServerSocket;

/** Ports for tests that need NATS to be unreachable. */
final class TestPorts {

    private TestPorts() {}

    /**
     * A local port nothing listens on (free at the time of the call).
     *
     * @return the port
     */
    static int closedPort() {
        try (var socket = new ServerSocket(0)) {
            return socket.getLocalPort();
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }
}
