package com.frappe.platform.infrastructure.nats;

import io.nats.client.Connection;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamManagement;
import io.nats.client.api.RetentionPolicy;
import io.nats.client.api.StorageType;
import io.nats.client.api.StreamConfiguration;
import java.io.IOException;
import java.time.Duration;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Creates or updates the {@code FRAPPE} stream so the server always matches this code. Safe to run repeatedly. */
final class NatsStreamProvisioner {

    /** Name of the one stream holding every Frappé event. */
    static final String STREAM = "FRAPPE";

    private static final Duration MAX_AGE = Duration.ofDays(7);
    private static final int REPLICAS = 1;
    // Re-publishes with the same Nats-Msg-Id inside this window are dropped; must cover retry and restart delays.
    private static final Duration DUPLICATE_WINDOW = Duration.ofMinutes(10);

    /** The stream exactly as the code wants it. */
    static final StreamConfiguration CONFIGURATION = StreamConfiguration.builder()
            .name(STREAM)
            .subjects(NatsSubjects.PREFIX + ".>")
            .retentionPolicy(RetentionPolicy.Limits)
            .storageType(StorageType.File)
            .maxAge(MAX_AGE)
            .replicas(REPLICAS)
            .duplicateWindow(DUPLICATE_WINDOW)
            .build();

    private static final Logger log = LoggerFactory.getLogger(NatsStreamProvisioner.class);
    private static final int NOT_FOUND = 404;

    /** Creates the provisioner. */
    NatsStreamProvisioner() {}

    /**
     * Creates the stream, or updates it to {@link #CONFIGURATION} when it exists.
     *
     * @param connection a live connection
     * @throws NatsProvisioningException if the server rejects the request or is unreachable
     */
    void provision(Connection connection) {
        try {
            var management = connection.jetStreamManagement();
            var outcome = exists(management) ? update(management) : create(management);
            log.atInfo()
                    .addKeyValue(LogFields.STREAM, STREAM)
                    .addKeyValue(LogFields.SUBJECT, CONFIGURATION.getSubjects())
                    .log("NATS stream {}", outcome);
        } catch (JetStreamApiException e) {
            throw new NatsProvisioningException(
                    "Cannot provision NATS stream " + STREAM + ": server error " + e.getErrorCode() + " "
                            + e.getErrorDescription(),
                    e);
        } catch (IOException e) {
            throw new NatsProvisioningException("Cannot provision NATS stream " + STREAM + ": " + e.getMessage(), e);
        }
    }

    private static String update(JetStreamManagement management) throws IOException, JetStreamApiException {
        management.updateStream(CONFIGURATION);
        return "up to date";
    }

    private static String create(JetStreamManagement management) throws IOException, JetStreamApiException {
        management.addStream(CONFIGURATION);
        return "created";
    }

    private static boolean exists(JetStreamManagement management) throws IOException, JetStreamApiException {
        try {
            management.getStreamInfo(STREAM);
            return true;
        } catch (JetStreamApiException e) {
            if (e.getErrorCode() != NOT_FOUND) {
                throw e;
            }
            return false;
        }
    }
}
