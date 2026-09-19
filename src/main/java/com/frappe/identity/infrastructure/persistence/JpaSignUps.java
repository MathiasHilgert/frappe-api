package com.frappe.identity.infrastructure.persistence;

import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.SignUp;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUps;
import java.util.Locale;
import java.util.Optional;
import java.util.UUID;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.springframework.stereotype.Repository;

/** {@link SignUps} on {@code identity.sign_up}, through JPA. */
@Repository
class JpaSignUps implements SignUps {

    private final SignUpRows rows;

    /**
     * Creates the adapter.
     *
     * @param rows the Spring Data repository
     */
    JpaSignUps(SignUpRows rows) {
        this.rows = rows;
    }

    @Override
    public Optional<SignUp> byId(SignUpId id) {
        return rows.findById(id.value()).map(JpaSignUps::toDomain);
    }

    @Override
    public Optional<SignUp> byEmailSubject(UUID emailSubject) {
        return rows.findByEmailSubject(emailSubject).map(JpaSignUps::toDomain);
    }

    @Override
    public void save(SignUp signUp) {
        var row = rows.findById(signUp.id().value())
                .orElseGet(() -> new SignUpEntity(signUp.id().value(), signUp.emailSubject()));
        if (row.version() != signUp.version()) {
            throw new ObjectOptimisticLockingFailureException(
                    SignUpEntity.class, signUp.id().value());
        }
        row.update(signUp.email().value(), signUp.locale().toLanguageTag(), signUp.startedAt());
        rows.save(row);
    }

    private static SignUp toDomain(SignUpEntity row) {
        return SignUp.reconstitute(
                new SignUpId(row.id()),
                new EmailAddress(row.email()),
                row.emailSubject(),
                Locale.forLanguageTag(row.locale()),
                row.startedAt(),
                row.version());
    }
}
