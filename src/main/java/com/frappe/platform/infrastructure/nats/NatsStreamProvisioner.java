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
class NatsStreamProvisioner {

    static final String STREAM = "FRAPPE";
    static final StreamConfiguration CONFIGURATION = StreamConfiguration.builder()
            .name(STREAM)
            .subjects(NatsSubjects.PREFIX + ".>")
            .retentionPolicy(RetentionPolicy.Limits)
            .storageType(StorageType.File)
            .maxAge(Duration.ofDays(7))
            .replicas(1)
            .duplicateWindow(Duration.ofMinutes(10))
            .build();

    private static final Logger log = LoggerFactory.getLogger(NatsStreamProvisioner.class);
    private static final int NOT_FOUND = 404;

    void provision(Connection connection) {
        try {
            var management = connection.jetStreamManagement();
            if (exists(management)) {
                management.updateStream(CONFIGURATION);
                log.atInfo()
                        .addKeyValue(LogFields.STREAM, STREAM)
                        .addKeyValue(LogFields.SUBJECT, CONFIGURATION.getSubjects())
                        .log("NATS stream up to date");
            } else {
                management.addStream(CONFIGURATION);
                log.atInfo()
                        .addKeyValue(LogFields.STREAM, STREAM)
                        .addKeyValue(LogFields.SUBJECT, CONFIGURATION.getSubjects())
                        .log("NATS stream created");
            }
        } catch (JetStreamApiException e) {
            throw new NatsProvisioningException(
                    "Cannot provision NATS stream " + STREAM + ": server error " + e.getErrorCode() + " "
                            + e.getErrorDescription(),
                    e);
        } catch (IOException e) {
            throw new NatsProvisioningException("Cannot provision NATS stream " + STREAM + ": " + e.getMessage(), e);
        }
    }

    private boolean exists(JetStreamManagement management) throws IOException, JetStreamApiException {
        try {
            management.getStreamInfo(STREAM);
            return true;
        } catch (JetStreamApiException e) {
            if (e.getErrorCode() == NOT_FOUND) {
                return false;
            }
            throw e;
        }
    }
}
