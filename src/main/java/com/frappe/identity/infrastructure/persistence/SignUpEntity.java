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

    UUID id() {
        return id;
    }

    String email() {
        return email;
    }

    UUID emailSubject() {
        return emailSubject;
    }

    String locale() {
        return locale;
    }

    Instant startedAt() {
        return startedAt;
    }

    long version() {
        return version == null ? 0 : version;
    }
}
