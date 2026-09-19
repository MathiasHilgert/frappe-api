package com.frappe.identity.infrastructure.persistence;

import com.frappe.identity.domain.SignUp;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUps;
import java.sql.Timestamp;
import java.util.Optional;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * {@link SignUps} on {@code identity.sign_up}: reads through JPA and {@link SignUpMapper}; a start is one
 * {@code insert ... on conflict (email_subject) do update} statement, so concurrent starts for one address all succeed,
 * each raising the version once, and the work is the same whether the address had a sign-up or not. The row's
 * {@code version} is raised by that statement, which also serves as the optimistic lock: no read-modify-write exists.
 */
@Repository
class JpaSignUps implements SignUps {

    private static final String UPSERT = """
            insert into identity.sign_up (id, email, email_subject, locale, started_at, version)
            values (:id, :email, :subject, :locale, :startedAt, 0)
            on conflict (email_subject) do update
                set email = excluded.email, locale = excluded.locale, started_at = excluded.started_at,
                    version = identity.sign_up.version + 1
            returning id, version
            """;

    private final SignUpRows rows;
    private final SignUpMapper mapper;
    private final JdbcClient jdbc;

    /**
     * Creates the adapter.
     *
     * @param rows the Spring Data repository, for reads
     * @param mapper rows to aggregates
     * @param jdbc the statement client, for the upsert
     */
    JpaSignUps(SignUpRows rows, SignUpMapper mapper, JdbcClient jdbc) {
        this.rows = rows;
        this.mapper = mapper;
        this.jdbc = jdbc;
    }

    @Override
    public Optional<SignUp> byId(SignUpId id) {
        return rows.findById(id.value()).map(mapper::toDomain);
    }

    @Override
    public SignUp record(SignUp start) {
        var stored = jdbc.sql(UPSERT)
                .param("id", start.id().value())
                .param("email", start.email().value())
                .param("subject", start.emailSubject())
                .param("locale", start.locale().toLanguageTag())
                .param("startedAt", Timestamp.from(start.startedAt()))
                .query((row, rowNumber) -> new Stored(row.getObject("id", UUID.class), row.getLong("version")))
                .single();
        return SignUp.reconstitute(
                new SignUpId(stored.id()),
                start.email(),
                start.emailSubject(),
                start.locale(),
                start.startedAt(),
                stored.version());
    }

    private record Stored(UUID id, long version) {}
}
