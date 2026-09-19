package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
@ActiveProfiles("local")
class FlywayMigrationIntegrationTests {

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void startupCreatesPlatformSchemaOwnedByOwnerRole() {
        var owner = jdbc.queryForObject(
                "select pg_get_userbyid(nspowner) from pg_namespace where nspname = 'platform'", String.class);

        assertThat(owner).isEqualTo("frappe_owner");
    }

    @Test
    void startupRecordsPlatformMigrationInSingleHistory() {
        var scripts = jdbc.queryForList(
                "select script from platform.flyway_schema_history where success and script like 'platform/%'",
                String.class);

        assertThat(scripts)
                .anyMatch(script -> script.matches("platform/V\\d{12}__create_platform_schema\\.sql"))
                .anyMatch(script -> script.matches("platform/V\\d{12}__create_event_publication_tables\\.sql"));
    }

    @Test
    void startupCreatesTheOutboxTablesOwnedByOwnerRole() {
        var owners = jdbc.queryForList(
                "select tableowner from pg_tables where schemaname = 'platform'"
                        + " and tablename in ('event_publication', 'event_publication_archive',"
                        + " 'event_publication_dead_letter')",
                String.class);

        assertThat(owners).containsExactly("frappe_owner", "frappe_owner", "frappe_owner");
    }

    @Test
    void outboxTablesCarryTheOfficialRegistryIndexes() {
        var indexes = jdbc.queryForList(
                "select indexname from pg_indexes"
                        + " where schemaname = 'platform' and tablename like 'event_publication%'",
                String.class);

        assertThat(indexes)
                .contains(
                        "event_publication_serialized_event_hash_idx",
                        "event_publication_by_completion_date_idx",
                        "event_publication_archive_serialized_event_hash_idx",
                        "event_publication_archive_by_completion_date_idx");
    }

    @Test
    void appRoleCannotRunDdlInPlatformSchema() {
        assertThatThrownBy(() -> jdbc.execute("create table platform.app_probe (id int)"))
                .isInstanceOf(DataAccessException.class)
                .rootCause()
                .hasMessageContaining("permission denied");
        assertThatThrownBy(() -> jdbc.execute("drop table platform.flyway_schema_history"))
                .isInstanceOf(DataAccessException.class)
                .rootCause()
                .hasMessageContaining("must be owner");
    }
}
