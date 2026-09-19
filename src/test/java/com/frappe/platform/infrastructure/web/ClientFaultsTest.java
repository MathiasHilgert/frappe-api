package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import jakarta.servlet.ServletException;
import java.io.EOFException;
import java.io.IOException;
import org.apache.catalina.connector.ClientAbortException;
import org.apache.coyote.BadRequestException;
import org.junit.jupiter.api.Test;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.http.converter.HttpMessageNotWritableException;
import org.springframework.mock.http.MockHttpInputMessage;

class ClientFaultsTest {

    @Test
    void aBodyThatCannotBeReadIsTheClientsFault() {
        var truncated = new HttpMessageNotReadableException(
                "JSON parse error", new EOFException(), new MockHttpInputMessage(new byte[0]));

        assertThat(ClientFaults.unreadableRequest(truncated)).isTrue();
        assertThat(ClientFaults.unreadableRequest(new IllegalStateException(new BadRequestException("bad chunk"))))
                .isTrue();
    }

    @Test
    void anAnswerTheClientNoLongerReadsIsTheClientBeingGone() {
        var aborted = new HttpMessageNotWritableException("Could not write JSON", new ClientAbortException());

        assertThat(ClientFaults.clientGone(aborted)).isTrue();
        assertThat(ClientFaults.unreadableRequest(aborted)).isFalse();
    }

    @Test
    void aBareIoExceptionIsNeverTakenForTheClient() {
        // Given what a database driver throws when its connection drops
        var driverFailure = new IllegalStateException(new IOException(new EOFException()));

        assertThat(ClientFaults.clientGone(driverFailure)).isFalse();
        assertThat(ClientFaults.unreadableRequest(driverFailure)).isFalse();
    }

    @Test
    void servletExceptionWrappersAreUnwrapped() {
        var cause = new IllegalStateException("filter failed");

        assertThat(ClientFaults.unwrap(new ServletException(new ServletException(cause))))
                .isSameAs(cause);
        assertThat(ClientFaults.unwrap(cause)).isSameAs(cause);
    }
}
