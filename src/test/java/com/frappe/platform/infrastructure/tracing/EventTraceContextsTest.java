package com.frappe.platform.infrastructure.tracing;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import io.micrometer.tracing.Tracer;
import io.micrometer.tracing.propagation.Propagator;
import java.time.Clock;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessResourceFailureException;

class EventTraceContextsTest {

    private static final UUID EVENT_ID = UUID.fromString("01923f5e-0000-7000-8000-000000000001");
    private static final W3cTraceContext RECORDED = W3cTraceContext.parse(
                    "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", "")
            .orElseThrow();

    private final EventTraceContextRepository repository = mock(EventTraceContextRepository.class);

    private final EventTraceContexts traceContexts =
            new EventTraceContexts(Tracer.NOOP, Propagator.NOOP, repository, Clock.systemUTC());

    private final Logger logger = (Logger) LoggerFactory.getLogger(EventTraceContexts.class);

    private final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        logger.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger.detachAppender(logs);
    }

    @Test
    void returnsTheRecordedTraceContext() {
        // Given
        when(repository.find(EVENT_ID)).thenReturn(Optional.of(RECORDED));

        // When / Then
        assertThat(traceContexts.recordedFor(EVENT_ID)).contains(RECORDED);
    }

    @Test
    void treatsAFailingLookupAsNoTraceContextAndWarnsWithTheEventId() {
        // Given
        when(repository.find(EVENT_ID)).thenThrow(new DataAccessResourceFailureException("connection refused"));

        // When
        var recorded = traceContexts.recordedFor(EVENT_ID);

        // Then publishing goes on without trace headers
        assertThat(recorded).isEmpty();
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getKeyValuePairs()).anySatisfy(pair -> {
                assertThat(pair.key).isEqualTo("frappe.event_id");
                assertThat(pair.value).isEqualTo(EVENT_ID);
            });
            assertThat(event.getThrowableProxy()).isNotNull();
        });
    }
}
