package com.frappe.identity.infrastructure.persistence;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.time.Instant;
import java.util.UUID;

/** A row of {@code identity.sign_up}. */
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

    // Null until persisted, so Spring Data inserts a new row instead of merging.
    @Version
    private Long version;

    /** For JPA. */
    protected SignUpEntity() {}

    /**
     * A new row.
     *
     * @param id the sign-up's id
     * @param emailSubject the keyed digest of the canonical address
     */
    SignUpEntity(UUID id, UUID emailSubject) {
        this.id = id;
        this.emailSubject = emailSubject;
    }

    /**
     * Copies the changeable state.
     *
     * @param enteredEmail the address as entered
     * @param languageTag the language to mail in
     * @param startedAtInstant when it last started
     */
    void update(String enteredEmail, String languageTag, Instant startedAtInstant) {
        this.email = enteredEmail;
        this.locale = languageTag;
        this.startedAt = startedAtInstant;
    }

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
     * @return the stored version, 0 before the first insert
     */
    long version() {
        return version == null ? 0 : version;
    }
}
