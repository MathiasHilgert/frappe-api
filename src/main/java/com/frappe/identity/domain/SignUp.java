package com.frappe.identity.domain;

import java.time.Instant;
import java.util.Locale;
import java.util.Objects;
import java.util.UUID;

/**
 * A started sign-up: the address as entered, its subject and the language to mail in, kept between "start" and
 * "complete". Every recorded start is announced by {@link SignUpStarted}, so a fresh code is mailed. It never becomes the
 * person. The store records a start as one upsert by subject, so it returns the stored id and version the event names.
 */
public final class SignUp {

    private final SignUpId id;
    private final EmailAddress email;
    private final UUID emailSubject;
    private final Locale locale;
    private final Instant startedAt;
    private final long version;

    private SignUp(SignUpId id, EmailAddress email, UUID emailSubject, Locale locale, Instant startedAt, long version) {
        this.id = Objects.requireNonNull(id, "id");
        this.email = Objects.requireNonNull(email, "email");
        this.emailSubject = Objects.requireNonNull(emailSubject, "emailSubject");
        this.locale = Objects.requireNonNull(locale, "locale");
        this.startedAt = Objects.requireNonNull(startedAt, "startedAt");
        this.version = version;
    }

    /**
     * A sign-up for an address, to be recorded: new, or replacing the address's stored one (the store keeps the stored
     * id and raises its version).
     *
     * @param id the id if the address has no sign-up yet
     * @param email the address as entered
     * @param emailSubject the keyed digest of the canonical address
     * @param locale the language to mail in
     * @param now when it starts
     * @return the sign-up to record, version 0
     */
    public static SignUp start(SignUpId id, EmailAddress email, UUID emailSubject, Locale locale, Instant now) {
        return new SignUp(id, email, emailSubject, locale, now, 0);
    }

    /**
     * Rebuilds a stored sign-up.
     *
     * @param id its id
     * @param email the address as entered
     * @param emailSubject the keyed digest of the canonical address
     * @param locale the language to mail in
     * @param startedAt when it last started
     * @param version the stored version
     * @return the sign-up, without events
     */
    public static SignUp reconstitute(
            SignUpId id, EmailAddress email, UUID emailSubject, Locale locale, Instant startedAt, long version) {
        return new SignUp(id, email, emailSubject, locale, startedAt, version);
    }

    /**
     * The event announcing this recorded start, so its code is mailed.
     *
     * @param eventId the event's id
     * @return the event naming this sign-up and its stored version
     */
    public SignUpStarted started(UUID eventId) {
        return new SignUpStarted(eventId, startedAt, id.value(), version, SignUpStarted.VERSION);
    }

    /**
     * Its id.
     *
     * @return the id
     */
    public SignUpId id() {
        return id;
    }

    /**
     * The address as entered.
     *
     * @return the address
     */
    public EmailAddress email() {
        return email;
    }

    /**
     * The keyed digest of the canonical address.
     *
     * @return the subject
     */
    public UUID emailSubject() {
        return emailSubject;
    }

    /**
     * The language to mail in.
     *
     * @return the locale
     */
    public Locale locale() {
        return locale;
    }

    /**
     * When it last started.
     *
     * @return the instant
     */
    public Instant startedAt() {
        return startedAt;
    }

    /**
     * The stored version, for optimistic locking.
     *
     * @return the version
     */
    public long version() {
        return version;
    }
}
