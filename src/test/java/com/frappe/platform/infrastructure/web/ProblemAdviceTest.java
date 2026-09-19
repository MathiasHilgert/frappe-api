package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.tracing.Tracer;
import java.io.EOFException;
import java.util.List;
import java.util.Locale;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.context.support.StaticMessageSource;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.mock.http.MockHttpInputMessage;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.web.context.request.ServletWebRequest;
import org.springframework.web.servlet.i18n.FixedLocaleResolver;

class ProblemAdviceTest {

    @Test
    void aClientFaultOnACommittedResponseIsLeftAloneWithoutAWarning() throws Exception {
        // Given
        var messages = new StaticMessageSource();
        messages.addMessage("platform.problem.invalid-request.title", Locale.ENGLISH, "Invalid request");
        messages.addMessage("platform.problem.invalid-request.detail", Locale.ENGLISH, "Unreadable");
        var advice = new ProblemAdvice(
                new Problems(messages, new FixedLocaleResolver(Locale.ENGLISH), Tracer.NOOP),
                new ProblemMappers(List.of()),
                new UnexpectedFailures(new SimpleMeterRegistry()));
        var response = new MockHttpServletResponse();
        response.setCommitted(true);
        var logs = new ListAppender<ILoggingEvent>();
        logs.start();
        var logger = (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);
        logger.addAppender(logs);
        try {
            // When the client's body broke after the answer started
            var answer = advice.handleException(
                    new HttpMessageNotReadableException(
                            "I/O error while reading input message",
                            new EOFException(),
                            new MockHttpInputMessage(new byte[0])),
                    new ServletWebRequest(new MockHttpServletRequest("POST", "/v1/orders"), response));

            // Then
            assertThat(answer).isNull();
            assertThat(logs.list).isEmpty();
        } finally {
            logger.detachAppender(logs);
        }
    }
}
