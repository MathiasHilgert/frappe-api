package com.frappe.identity.infrastructure.persistence;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.time.Instant;
import java.util.UUID;

/** A row of {@code identity.sign_up}, read only: starts are written by one upsert statement ({@link JpaSignUps}). */
@Entity
@Table(schema = "identity", name = "sign_up")
class SignUpEntity {

    @Id
    private UUID id;

    @Column(nullable = false)
    private String email;

    @Column(name = "email_subject", nullable = false, updatable = false)
    private UUID emailSubject;

    @Column(nullable = false)
    private String locale;

    @Column(name = "started_at", nullable = false)
    private Instant startedAt;

    // Raised by the upsert on every restart.
    @Version
    private long version;

    /** For JPA. */
    protected SignUpEntity() {}

    /**
     * The column value.
     *
     * @return its id
     */
    UUID id() {
        return id;
    }

    /**
     * The column value.
     *
     * @return the address as entered
     */
    String email() {
        return email;
    }

    /**
     * The column value.
     *
     * @return the keyed digest of the canonical address
     */
    UUID emailSubject() {
        return emailSubject;
    }

    /**
     * The column value.
     *
     * @return the language tag to mail in
     */
    String locale() {
        return locale;
    }

    /**
     * The column value.
     *
     * @return when it last started
     */
    Instant startedAt() {
        return startedAt;
    }

    /**
     * The column value.
     *
     * @return the stored version
     */
    long version() {
        return version;
    }
}
