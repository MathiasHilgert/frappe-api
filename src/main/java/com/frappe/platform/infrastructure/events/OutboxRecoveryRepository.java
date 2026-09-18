package com.frappe.platform.infrastructure.events;

import java.sql.Timestamp;
import java.time.Instant;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * Recovery queries on the outbox tables that Spring Modulith's registry does not offer. Owns SQL on {@code
 * platform.event_publication} only for recovery; the registry stays the writer of the normal lifecycle.
 */
@Repository
class OutboxRecoveryRepository {

    // A publication without outcome is judged by its latest attempt: the resubmission time if it was resubmitted,
    // otherwise its publication time. Modulith's own staleness monitor uses the publication time for every status,
    // which would re-fail an old event in the middle of its resubmission. The status guard keeps the update from
    // touching a row that completed or failed meanwhile.
    private static final String RELEASE_STUCK = """
            update platform.event_publication
               set status = 'FAILED'
             where status in ('PUBLISHED', 'PROCESSING', 'RESUBMITTED')
               and coalesce(last_resubmission_date, publication_date) < ?
            """;

    private final JdbcClient jdbc;

    /**
     * Creates the repository.
     *
     * @param jdbc client on the application data source
     */
    OutboxRecoveryRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    /**
     * Marks publications whose latest attempt started before the threshold and never reported an outcome as failed,
     * so they become eligible for resubmission.
     *
     * @param attemptStartedBefore attempts started before this instant are considered stuck
     * @return the number of released publications
     */
    int releaseStuckPublications(Instant attemptStartedBefore) {
        return jdbc.sql(RELEASE_STUCK)
                .param(Timestamp.from(attemptStartedBefore))
                .update();
    }
}
