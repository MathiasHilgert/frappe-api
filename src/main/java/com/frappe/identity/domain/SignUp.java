package com.frappe.identity.domain;

import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Objects;
import java.util.UUID;

/**
 * A started sign-up: the address as entered, its subject and the language to mail in, kept between "start" and
 * "complete". Every start publishes {@link SignUpStarted}, so a fresh code is mailed. It never becomes the person.
 */
public final class SignUp {

    private final SignUpId id;
    private EmailAddress email;
    private final UUID emailSubject;
    private Locale locale;
    private Instant startedAt;
    private final long version;
    private final List<DomainEvent> events = new ArrayList<>();

    private SignUp(SignUpId id, EmailAddress email, UUID emailSubject, Locale locale, Instant startedAt, long version) {
        this.id = Objects.requireNonNull(id, "id");
        this.email = Objects.requireNonNull(email, "email");
        this.emailSubject = Objects.requireNonNull(emailSubject, "emailSubject");
        this.locale = Objects.requireNonNull(locale, "locale");
        this.startedAt = Objects.requireNonNull(startedAt, "startedAt");
        this.version = version;
    }

    /**
     * Starts a sign-up for an address that has none.
     *
     * @param id the new sign-up's id
     * @param email the address as entered
     * @param emailSubject the keyed digest of the canonical address
     * @param locale the language to mail in
     * @param now when it starts
     * @param eventId the id of the event it publishes
     * @return the started sign-up, holding its {@link SignUpStarted}
     */
    public static SignUp start(
            SignUpId id, EmailAddress email, UUID emailSubject, Locale locale, Instant now, UUID eventId) {
        var signUp = new SignUp(id, email, emailSubject, locale, now, 0);
        signUp.events.add(new SignUpStarted(eventId, now, id.value(), 0, SignUpStarted.VERSION));
        return signUp;
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
     * Starts the sign-up again: the latest address as entered and language win, and a new code is to be mailed.
     *
     * @param enteredEmail the address as entered this time
     * @param newLocale the language to mail in
     * @param now when it starts again
     * @param eventId the id of the event it publishes
     */
    public void restart(EmailAddress enteredEmail, Locale newLocale, Instant now, UUID eventId) {
        email = Objects.requireNonNull(enteredEmail, "enteredEmail");
        locale = Objects.requireNonNull(newLocale, "newLocale");
        startedAt = Objects.requireNonNull(now, "now");
        // The stored version grows by one with this change, so the event names the version it produces.
        events.add(new SignUpStarted(eventId, now, id.value(), version + 1, SignUpStarted.VERSION));
    }

    /**
     * Hands over the events registered since the last call.
     *
     * @return the events, oldest first
     */
    public List<DomainEvent> pullEvents() {
        var pulled = List.copyOf(events);
        events.clear();
        return pulled;
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
