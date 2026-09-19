package com.frappe.identity.infrastructure.persistence;

import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.Person;
import com.frappe.identity.domain.Persons;
import com.frappe.platform.Result;
import java.sql.Timestamp;
import java.util.Objects;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * {@link Persons} on {@code identity.person} and {@code identity.recovery_code}, through plain statements: a new person
 * is one insert plus one per recovery code, and nothing reads the aggregate back yet. The unique index on
 * {@code lower(email)} decides a race for one address: the later insert waits for the earlier commit and then violates
 * it, which is answered as {@link Persons.EmailTaken}.
 */
@Repository
class JdbcPersons implements Persons {

    private static final String EMAIL_KEY = "person_email_key";

    private static final String INSERT_PERSON = """
            insert into identity.person (id, email, status, password_hash, credential_epoch, given_name, family_name,
                preferred_locale, terms_version, privacy_version, legal_accepted_at, registered_at, version)
            values (:id, :email, :status, :passwordHash, :epoch, :givenName, :familyName, :locale, :terms, :privacy,
                :acceptedAt, :registeredAt, :version)
            """;

    private static final String INSERT_RECOVERY_CODE =
            "insert into identity.recovery_code (id, person_id, digest) values (:id, :personId, :digest)";

    private static final String IS_REGISTERED =
            "select exists (select 1 from identity.person where lower(email) = lower(:email))";

    private final JdbcClient jdbc;

    /**
     * Creates the adapter.
     *
     * @param jdbc the statement client
     */
    JdbcPersons(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    @Override
    public Result<Person, EmailTaken> add(Person person) {
        try {
            insert(person);
        } catch (DuplicateKeyException e) {
            if (!Objects.toString(e.getMessage(), "").contains(EMAIL_KEY)) {
                throw e;
            }
            return Result.failure(new EmailTaken());
        }
        for (var code : person.recoveryCodes()) {
            jdbc.sql(INSERT_RECOVERY_CODE)
                    .param("id", code.id())
                    .param("personId", person.id().value())
                    .param("digest", code.digest())
                    .update();
        }
        return Result.success(person);
    }

    @Override
    public boolean isRegistered(EmailAddress email) {
        return jdbc.sql(IS_REGISTERED)
                .param("email", email.value())
                .query(Boolean.class)
                .single();
    }

    private void insert(Person person) {
        var legal = person.legalAcceptance();
        jdbc.sql(INSERT_PERSON)
                .param("id", person.id().value())
                .param("email", person.email().value())
                .param("status", person.status().name())
                .param("passwordHash", person.passwordHash().value())
                .param("epoch", person.credentialEpoch())
                .param("givenName", person.givenName().value())
                .param("familyName", person.familyName().value())
                .param("locale", person.preferredLocale().toLanguageTag())
                .param("terms", legal.termsVersion())
                .param("privacy", legal.privacyVersion())
                .param("acceptedAt", Timestamp.from(legal.acceptedAt()))
                .param("registeredAt", Timestamp.from(person.registeredAt()))
                .param("version", person.version())
                .update();
    }
}
