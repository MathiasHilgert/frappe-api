package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import io.nats.client.Connection;
import java.util.function.Consumer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.ApplicationEventPublisher;

/**
 * Runs after every (re)connect: makes the stream match the code, then announces {@link MessagingTransportRecovered} so
 * the outbox recovery resubmits publications that failed while NATS was away. Without it they would wait for the next
 * scheduled recovery run.
 */
final class NatsConnectSetup implements Consumer<Connection> {

    private static final Logger log = LoggerFactory.getLogger(NatsConnectSetup.class);

    private final NatsStreamProvisioner provisioner;
    private final ApplicationEventPublisher events;

    /**
     * Creates the setup.
     *
     * @param provisioner creates or updates the stream
     * @param events announces the recovered transport
     */
    NatsConnectSetup(NatsStreamProvisioner provisioner, ApplicationEventPublisher events) {
        this.provisioner = provisioner;
        this.events = events;
    }

    @Override
    public void accept(Connection connection) {
        try {
            provisioner.provision(connection);
            events.publishEvent(MessagingTransportRecovered.NATS);
        } catch (NatsProvisioningException e) {
            // Runs on a background executor: this is the last place the failure can be reported.
            log.atError()
                    .setCause(e)
                    .log(
                            "NATS connected but stream setup failed; publications are retried by the scheduled outbox recovery");
        }
    }
}
