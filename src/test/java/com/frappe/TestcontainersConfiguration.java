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
        return new PostgreSQLContainer(DockerImageName.parse("postgres:18-alpine"))
                .withDatabaseName(db)
                .withCopyFileToContainer(
                        MountableFile.forHostPath("docker/postgres/initdb/01-frappe-roles.sh", 0755),
                        "/docker-entrypoint-initdb.d/01-frappe-roles.sh")
                .withReuse(true);
    }

    @Bean
    DynamicPropertyRegistrar postgresProperties(PostgreSQLContainer postgres) {
        return registry -> {
            registry.add("spring.datasource.url", postgres::getJdbcUrl);
            // Spring caches one context per distinct test configuration, each with its own pool; Hikari's default of
            // 10 connections per context exhausts Postgres' 100 slots once the suite holds more than a few contexts.
            registry.add("spring.datasource.hikari.maximum-pool-size", () -> TEST_POOL_SIZE);
        };
    }
}
