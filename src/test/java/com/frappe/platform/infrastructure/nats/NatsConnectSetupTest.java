package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doAnswer;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import io.nats.client.Connection;
import java.util.ArrayList;
import java.util.function.Predicate;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.support.StaticListableBeanFactory;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.IncompleteEventPublications;
import tools.jackson.core.exc.StreamReadException;

class NatsConnectSetupTest {

    @Test
    void anUnreadablePublicationDoesNotStopTheResubmissionOnReconnect() {
        // Given
        var externalization = mock(EventExternalizationConfiguration.class);
        when(externalization.supports(any())).thenReturn(true);
        var readable = failedPublication();
        when(readable.getEvent()).thenReturn(new Object());
        var unreadable = failedPublication();
        when(unreadable.getEvent()).thenThrow(new StreamReadException(null, "Unexpected end-of-input"));
        var incomplete = mock(IncompleteEventPublications.class);
        var resubmitted = new ArrayList<EventPublication>();
        doAnswer(call -> {
                    Predicate<EventPublication> filter = call.getArgument(0);
                    Stream.of(unreadable, readable).filter(filter).forEach(resubmitted::add);
                    return null;
                })
                .when(incomplete)
                .resubmitIncompletePublications(any(Predicate.class));
        var beans = new StaticListableBeanFactory();
        beans.addBean("incomplete", incomplete);
        var setup = new NatsConnectSetup(
                mock(NatsStreamProvisioner.class),
                externalization,
                beans.getBeanProvider(IncompleteEventPublications.class));

        // When
        setup.accept(mock(Connection.class));

        // Then
        // The outbox recovery job dead-letters the unreadable row; here it is only skipped.
        assertThat(resubmitted).containsExactly(readable);
    }

    private static EventPublication failedPublication() {
        var publication = mock(EventPublication.class);
        when(publication.getStatus()).thenReturn(EventPublication.Status.FAILED);
        return publication;
    }
}
