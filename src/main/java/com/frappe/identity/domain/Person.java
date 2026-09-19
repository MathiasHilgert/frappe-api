package com.frappe.identity.domain;

import java.time.Instant;
import java.util.List;
import java.util.Locale;
import java.util.Objects;

/**
 * A person with an account: their proven address, password hash, credential epoch, names, preferred language, legal
 * acceptance and recovery codes. Changing any of them writes this one aggregate; raising the epoch revokes every
 * session in that same write.
 */
public final class Person {

    private final PersonId id;
    private final EmailAddress email;
    private final PersonStatus status;
    private final PasswordHash passwordHash;
    private final long credentialEpoch;
    private final PersonName givenName;
    private final PersonName familyName;
    private final Locale preferredLocale;
    private final LegalAcceptance legalAcceptance;
    private final List<RecoveryCode> recoveryCodes;
    private final Instant registeredAt;
    private final long version;

    private Person(
            PersonId id,
            EmailAddress email,
            PersonStatus status,
            PasswordHash passwordHash,
            long credentialEpoch,
            PersonName givenName,
            PersonName familyName,
            Locale preferredLocale,
            LegalAcceptance legalAcceptance,
            List<RecoveryCode> recoveryCodes,
            Instant registeredAt,
            long version) {
        this.id = Objects.requireNonNull(id, "id");
        this.email = Objects.requireNonNull(email, "email");
        this.status = Objects.requireNonNull(status, "status");
        this.passwordHash = Objects.requireNonNull(passwordHash, "passwordHash");
        this.credentialEpoch = credentialEpoch;
        this.givenName = Objects.requireNonNull(givenName, "givenName");
        this.familyName = Objects.requireNonNull(familyName, "familyName");
        this.preferredLocale = Objects.requireNonNull(preferredLocale, "preferredLocale");
        this.legalAcceptance = Objects.requireNonNull(legalAcceptance, "legalAcceptance");
        this.recoveryCodes = List.copyOf(recoveryCodes);
        this.registeredAt = Objects.requireNonNull(registeredAt, "registeredAt");
        this.version = version;
    }

    /**
     * Registers a person whose address a code just proved: active at once, at credential epoch 1.
     *
     * @param id the new person's id
     * @param email the proven address
     * @param passwordHash the hash of the chosen password
     * @param givenName the given name
     * @param familyName the family name
     * @param preferredLocale the language the person signed up in
     * @param legalAcceptance the accepted legal versions and when
     * @param recoveryCodes exactly {@value RecoveryCodes#COUNT} recovery codes with distinct digests
     * @param now when the person registers
     * @return the person, version 0
     * @throws IllegalArgumentException if the recovery codes are not {@value RecoveryCodes#COUNT} distinct digests
     */
    public static Person register(
            PersonId id,
            EmailAddress email,
            PasswordHash passwordHash,
            PersonName givenName,
            PersonName familyName,
            Locale preferredLocale,
            LegalAcceptance legalAcceptance,
            List<RecoveryCode> recoveryCodes,
            Instant now) {
        var digests = recoveryCodes.stream().map(RecoveryCode::digest).distinct().count();
        if (recoveryCodes.size() != RecoveryCodes.COUNT || digests != RecoveryCodes.COUNT) {
            throw new IllegalArgumentException(
                    "a person registers with " + RecoveryCodes.COUNT + " distinct recovery codes");
        }
        return new Person(
                id,
                email,
                PersonStatus.ACTIVE,
                passwordHash,
                1,
                givenName,
                familyName,
                preferredLocale,
                legalAcceptance,
                recoveryCodes,
                now,
                0);
    }

    /**
     * Its id.
     *
     * @return the id
     */
    public PersonId id() {
        return id;
    }

    /**
     * The proven address, as entered.
     *
     * @return the address
     */
    public EmailAddress email() {
        return email;
    }

    /**
     * Whether the account works.
     *
     * @return the status
     */
    public PersonStatus status() {
        return status;
    }

    /**
     * The hash of the password.
     *
     * @return the hash
     */
    public PasswordHash passwordHash() {
        return passwordHash;
    }

    /**
     * The credential epoch every session is stamped with; raising it revokes them all.
     *
     * @return the epoch, from 1
     */
    public long credentialEpoch() {
        return credentialEpoch;
    }

    /**
     * The given name.
     *
     * @return the name
     */
    public PersonName givenName() {
        return givenName;
    }

    /**
     * The family name.
     *
     * @return the name
     */
    public PersonName familyName() {
        return familyName;
    }

    /**
     * The language the person prefers.
     *
     * @return the locale
     */
    public Locale preferredLocale() {
        return preferredLocale;
    }

    /**
     * The accepted legal versions and when.
     *
     * @return the acceptance
     */
    public LegalAcceptance legalAcceptance() {
        return legalAcceptance;
    }

    /**
     * The stored recovery codes.
     *
     * @return their digests
     */
    public List<RecoveryCode> recoveryCodes() {
        return recoveryCodes;
    }

    /**
     * When the person registered.
     *
     * @return the instant
     */
    public Instant registeredAt() {
        return registeredAt;
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
