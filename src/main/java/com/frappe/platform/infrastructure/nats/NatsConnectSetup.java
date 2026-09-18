package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
import java.util.function.Consumer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.dao.DataAccessException;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.IncompleteEventPublications;

/**
 * Runs after every (re)connect: makes the stream match the code, then resubmits externalized publications that
 * failed while NATS was away. Without the resubmission they would wait for a restart or a manual retry.
 */
final class NatsConnectSetup implements Consumer<Connection> {

    private static final Logger log = LoggerFactory.getLogger(NatsConnectSetup.class);

    private final NatsStreamProvisioner provisioner;
    private final EventExternalizationConfiguration externalization;
    private final ObjectProvider<IncompleteEventPublications> incompletePublications;

    /**
     * Creates the setup.
     *
     * @param provisioner creates or updates the stream
     * @param externalization decides which publications belong to NATS
     * @param incompletePublications resubmission entry point of the publication registry, looked up lazily
     */
    NatsConnectSetup(
            NatsStreamProvisioner provisioner,
            EventExternalizationConfiguration externalization,
            ObjectProvider<IncompleteEventPublications> incompletePublications) {
        this.provisioner = provisioner;
        this.externalization = externalization;
        this.incompletePublications = incompletePublications;
    }

    @Override
    public void accept(Connection connection) {
        try {
            provisioner.provision(connection);
            incompletePublications.ifAvailable(
                    publications -> publications.resubmitIncompletePublications(this::isFailedNatsPublication));
        } catch (NatsProvisioningException | DataAccessException e) {
            // Runs on a background executor: this is the last place the failure can be reported.
            log.atError()
                    .setCause(e)
                    .log("NATS connected but setup failed; publications stay incomplete until the next connect");
        }
    }

    private boolean isFailedNatsPublication(EventPublication publication) {
        return publication.getStatus() == EventPublication.Status.FAILED
                && externalization.supports(publication.getEvent());
    }
}
