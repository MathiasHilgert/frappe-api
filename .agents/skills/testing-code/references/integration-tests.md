# Integration tests

Scope: persistence adapters, Flyway migrations, RLS, HTTP end to end, NATS relay. Generic Spring Boot detail: skill `322-frameworks-spring-boot-testing-integration-tests`.

## Containers

- Declare containers as `@Bean` in `TestcontainersConfiguration` (Postgres, reused), `TestNatsConfiguration` (NATS, fresh per context, not reused, so tests may pause it or change the stream) and `TestValkeyConfiguration` (Valkey, fresh per context); tests `@Import` what they need. Match production images (`postgres:18-alpine`, `nats:2.12-alpine`, `valkey/valkey:9-alpine`).
- Valkey: `RedisContainer` (`com.redis:testcontainers-redis`) on the valkey image with `@ServiceConnection(name = "redis")`, because Boot does not recognise the image by name. Import it wherever a test touches `ShortLivedSecretStore` or `RateLimiter`; contexts without it still start (connections are lazy) and see those ports fail with `SecretStoreUnavailableException`. Tests use fresh subject ids per test and never assume an empty store. Time-dependent behaviour (issue windows, bucket refills) is driven by a clock the test moves, passed to the adapter; only TTL expiry, which Valkey's own clock enforces, waits in real time (short TTLs).
- Log format tests: capture events with a Logback `ListAppender` and render them with Boot's `StructuredLogEncoder`; never re-initialize the JVM-wide logging system (cached contexts share it).
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
- Outbox tests (`platform.event_publication*`) share the tables with every cached context, whose recovery job keeps running in the background: assert only on rows of the test's own `eventId` (or publication id), never on counts or ordering of the whole table. Rows inserted by hand and dated so the job never picks them up (e.g. year 2100) must be deleted in `@AfterEach`, or they pollute later runs.
- A reused database keeps its Flyway history. When a migration it already applied is removed or edited (FAPI-6 deleted the FAPI-5 `event_publication` stopgap fixture), startup fails with `FlywayValidateException: Migrations have failed validation`. Remove that ticket's container and rerun; the next run creates it fresh:

```bash
for c in $(docker ps -q --filter label=org.testcontainers=true --filter ancestor=postgres:18-alpine); do
  docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$c" | rg -q '^POSTGRES_DB=frappe_fapi_12$' && docker rm -f "$c"
done
```

## RLS tests

For every tenant-scoped table:

1. Insert rows for tenant A and tenant B as the app role.
2. `set local app.tenant_id` to A inside a transaction.
3. Assert only A's rows are visible and updates to B's rows affect 0 rows.
4. Assert a query without `app.tenant_id` set fails or returns nothing.

Run as the application role, not the superuser/owner, or RLS is bypassed and the test proves nothing.

## HTTP

Use `MockMvcTester` or `RestTestClient` against the full module; assert status, `ProblemDetail` body (`type`, `status`) and `Location` headers.
