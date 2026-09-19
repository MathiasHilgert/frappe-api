package com.frappe;

import java.util.Optional;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.test.context.DynamicPropertyRegistrar;
import org.testcontainers.postgresql.PostgreSQLContainer;
import org.testcontainers.utility.DockerImageName;
import org.testcontainers.utility.MountableFile;

/**
 * Real Postgres 18 bootstrapped with the same role script as compose.yaml. No {@code @ServiceConnection}: it would
 * connect as the container superuser, so the app and Flyway credentials come from application.properties instead.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TestcontainersConfiguration {

    private static final int TEST_POOL_SIZE = 4;

    @Bean
    public PostgreSQLContainer postgresContainer() {
        // One database name per worktree (FRAPPE_TEST_DB=frappe_fapi_<n>): reused containers are keyed by
        // their configuration, so worktrees sharing a name would share one container and its data.
        var db = Optional.ofNullable(System.getenv("FRAPPE_TEST_DB")).orElse("frappe");
        return postgres(db).withReuse(true);
    }

    @Bean
    DynamicPropertyRegistrar postgresProperties(PostgreSQLContainer postgres) {
        return connectionProperties(postgres);
    }

    /**
     * A Postgres 18 container with the application roles, not reused. Tests whose outbox rows must not be seen by the
     * recovery jobs of other cached contexts (which share the reused database) run on one of these instead.
     *
     * @param databaseName the database to create
     * @return the container, not started
     */
    public static PostgreSQLContainer postgres(String databaseName) {
        return new PostgreSQLContainer(DockerImageName.parse("postgres:18-alpine"))
                .withDatabaseName(databaseName)
                .withCopyFileToContainer(
                        MountableFile.forHostPath("docker/postgres/initdb/01-frappe-roles.sh", 0755),
                        "/docker-entrypoint-initdb.d/01-frappe-roles.sh");
    }

    /**
     * Points the application (as {@code frappe_app}) and Flyway (as {@code frappe_owner}) at a container.
     *
     * @param postgres the container
     * @return the registrar
     */
    public static DynamicPropertyRegistrar connectionProperties(PostgreSQLContainer postgres) {
        return registry -> {
            registry.add("spring.datasource.url", postgres::getJdbcUrl);
            // Spring caches one context per distinct test configuration, each with its own pool; Hikari's default of
            // 10 connections per context exhausts Postgres' 100 slots once the suite holds more than a few contexts.
            registry.add("spring.datasource.hikari.maximum-pool-size", () -> TEST_POOL_SIZE);
        };
    }
}
