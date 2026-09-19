package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
@ActiveProfiles("local")
class IdentitySchemaIntegrationTests {

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void startupCreatesTheIdentitySchemaOwnedByTheOwnerRole() {
        var owner = jdbc.queryForObject(
                "select pg_get_userbyid(nspowner) from pg_namespace where nspname = 'identity'", String.class);

        assertThat(owner).isEqualTo("frappe_owner");
    }

    @Test
    void startupRecordsTheIdentityMigrationInTheSingleHistory() {
        var scripts = jdbc.queryForList(
                "select script from platform.flyway_schema_history where success and script like 'identity/%'",
                String.class);

        assertThat(scripts).anyMatch(script -> script.matches("identity/V\\d{12}__create_identity_schema\\.sql"));
    }

    @Test
    void anActivePersonWithoutAPasswordHashIsRefusedByTheDatabase() {
        assertThatExceptionOfType(DataIntegrityViolationException.class).isThrownBy(() -> jdbc.update("""
                insert into identity.person (id, email, status, credential_epoch, given_name, family_name,
                    preferred_locale, terms_version, privacy_version, legal_accepted_at, registered_at, version)
                values (gen_random_uuid(), 'x@example.com', 'ACTIVE', 1, 'A', 'B', 'en', 't', 'p', now(), now(), 0)
                """));
    }

    @Test
    void theNewIdentityTablesAreOwnedByTheOwnerRoleAndWritableByTheApp() {
        var rows = jdbc.queryForList("""
                select tableowner, has_table_privilege('frappe_app', schemaname || '.' || tablename, 'INSERT')
                from pg_tables where schemaname = 'identity' and tablename in ('person', 'recovery_code')
                """);

        assertThat(rows).hasSize(2).allSatisfy(row -> assertThat(row.values()).containsExactly("frappe_owner", true));
    }
}
