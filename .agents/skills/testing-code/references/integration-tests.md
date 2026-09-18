# Integration tests

Scope: persistence adapters, Flyway migrations, RLS, HTTP end to end, NATS relay. Generic Spring Boot detail: skill `322-frameworks-spring-boot-testing-integration-tests`.

## Containers

- Declare containers as `@Bean` in `TestcontainersConfiguration`; tests `@Import` it. Match production images (`postgres:18-alpine`, `nats:2.12-alpine`).
- Postgres is the exception to `@ServiceConnection`: it would connect as the container superuser. The container runs the roles init script and only `spring.datasource.url` is registered, so the app connects as `frappe_app` and Flyway as `frappe_owner`, exactly as in production.
- Never H2 or embedded substitutes: RLS, schemas and SQL dialect must be real.
- Enable reuse so runs and worktrees skip container startup:
  - `~/.testcontainers.properties`: `testcontainers.reuse.enable=true`
  - container: `.withReuse(true)`

## One database per ticket

Name the database from the environment so parallel worktrees never share data:

```java
@Bean
PostgreSQLContainer postgres() {
    var db = Optional.ofNullable(System.getenv("FRAPPE_TEST_DB")).orElse("frappe");
    return new PostgreSQLContainer(DockerImageName.parse("postgres:18-alpine"))
            .withDatabaseName(db).withReuse(true);
}
```

- Run with `FRAPPE_TEST_DB=frappe_fapi_12 ./gradlew check` in the FAPI-12 worktree.
- The reuse key includes the configuration, so each database name gets its own long-lived container, reused across runs of that ticket. Remove it after merge (`running-in-parallel.md` in `working-on-tickets`).
- Reused containers keep data between runs: tests create their own tenant/IDs (UUIDv7) and never assume empty tables.

## RLS tests

For every tenant-scoped table:

1. Insert rows for tenant A and tenant B as the app role.
2. `set local app.tenant_id` to A inside a transaction.
3. Assert only A's rows are visible and updates to B's rows affect 0 rows.
4. Assert a query without `app.tenant_id` set fails or returns nothing.

Run as the application role, not the superuser/owner, or RLS is bypassed and the test proves nothing.

## HTTP

Use `MockMvcTester` or `RestTestClient` against the full module; assert status, `ProblemDetail` body (`type`, `status`) and `Location` headers.
