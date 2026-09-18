package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.TestcontainersConfiguration;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;

/**
 * Uses {@code src/test/resources/db/migration/fixture}, a later migration with no GRANT of its own, as a stand-in for
 * any module table added after the roles were bootstrapped.
 */
@Import(TestcontainersConfiguration.class)
@SpringBootTest
class DefaultPrivilegesIntegrationTests {

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void appRoleHasDmlOnTablesFromLaterMigrationsWithoutExtraGrant() {
        var id = UUID.randomUUID();

        assertThat(jdbc.update("insert into fixture.probe (id, label) values (?, 'new')", id))
                .isOne();
        assertThat(jdbc.queryForObject("select label from fixture.probe where id = ?", String.class, id))
                .isEqualTo("new");
        assertThat(jdbc.update("update fixture.probe set label = 'changed' where id = ?", id))
                .isOne();
        assertThat(jdbc.update("delete from fixture.probe where id = ?", id)).isOne();
    }

    @ParameterizedTest
    @ValueSource(
            strings = {
                "create table fixture.app_probe (id int)",
                "alter table fixture.probe add column extra int",
                "drop table fixture.probe",
                "truncate fixture.probe",
            })
    void appRoleCannotRunDdlOnTablesFromLaterMigrations(String ddl) {
        assertThatThrownBy(() -> jdbc.execute(ddl))
                .isInstanceOf(DataAccessException.class)
                .rootCause()
                .hasMessageMatching("(?s).*(permission denied|must be owner).*");
    }
}
