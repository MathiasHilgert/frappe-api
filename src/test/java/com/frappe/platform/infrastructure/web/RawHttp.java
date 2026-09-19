package com.frappe.platform.infrastructure.web;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;

/** Sends bytes no HTTP client library would send, and reads whatever the server answers until it closes. */
final class RawHttp {

    private RawHttp() {}

    /**
     * Sends a raw request, half-closes the connection and reads the answer.
     *
     * @param port the server's port
     * @param request the bytes to send, as ISO-8859-1 text
     * @return the raw answer (status line, headers, body); empty when the server answered nothing
     */
    static String exchange(int port, String request) throws IOException {
        try (var socket = new Socket("localhost", port)) {
            socket.setSoTimeout(10_000);
            socket.getOutputStream().write(request.getBytes(StandardCharsets.ISO_8859_1));
            socket.getOutputStream().flush();
            socket.shutdownOutput();
            var answer = new ByteArrayOutputStream();
            try {
                socket.getInputStream().transferTo(answer);
            } catch (SocketTimeoutException | java.net.SocketException closed) {
                // what arrived before the server stopped answering is the answer
            }
            return answer.toString(StandardCharsets.UTF_8);
        }
    }
}
