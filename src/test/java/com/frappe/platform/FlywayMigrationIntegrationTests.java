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

        assertThat(scripts).singleElement().asString().matches("platform/V\\d{12}__create_platform_schema\\.sql");
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
