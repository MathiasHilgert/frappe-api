package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
class DatabaseRolesIntegrationTests {

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void bootstrapCreatesOwnerAndAppRoles() {
        var roles = jdbc.queryForList(
                "select rolname from pg_roles where rolname in ('frappe_owner', 'frappe_app')", String.class);

        assertThat(roles).containsExactlyInAnyOrder("frappe_owner", "frappe_app");
    }

    @Test
    void applicationConnectsAsUnprivilegedAppRole() {
        var role = jdbc.queryForMap("select current_user as name, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls"
                + " from pg_roles where rolname = current_user");

        assertThat(role)
                .containsEntry("name", "frappe_app")
                .containsEntry("rolsuper", false)
                .containsEntry("rolcreaterole", false)
                .containsEntry("rolcreatedb", false)
                .containsEntry("rolbypassrls", false);
    }

    @ParameterizedTest
    @ValueSource(
            strings = {
                "create table public.app_probe (id int)",
                "create schema app_probe",
                "create temporary table app_probe (id int)",
            })
    void appRoleCannotRunDdl(String ddl) {
        assertThatThrownBy(() -> jdbc.execute(ddl))
                .isInstanceOf(DataAccessException.class)
                .rootCause()
                .hasMessageContaining("permission denied");
    }
}
