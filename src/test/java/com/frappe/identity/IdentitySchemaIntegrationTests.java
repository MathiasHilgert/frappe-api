package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
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
}
