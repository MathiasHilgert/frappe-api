# Running in parallel

## Worktree per ticket

```bash
git -C ~/Projects/nulled/frappe-api fetch origin
git -C ~/Projects/nulled/frappe-api worktree add \
  ~/Projects/nulled/frappe-api-worktrees/FAPI-12 -b feat/fapi-12-person-sessions origin/main
```

- Branch: `<type>/fapi-N-<slug>`, type from the ticket's `type:*` label (`feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`).
- The writer works only inside its worktree path.

## Shared infrastructure

- Start Docker services once from the main checkout: `docker compose up -d`. Never from a worktree, never per agent.
- Testcontainers reuse: ensure `~/.testcontainers.properties` contains `testcontainers.reuse.enable=true`; containers declare `.withReuse(true)`. All worktrees then share one Postgres and one NATS container.
- One database per ticket: `frappe_fapi_12` for FAPI-12. Create it on first use and point the test datasource at it (see `testing-code` → `integration-tests.md`).
- Gradle cache (`~/.gradle`) is shared; do not set a per-worktree `GRADLE_USER_HOME`.

## Cleanup after merge

```bash
git -C ~/Projects/nulled/frappe-api worktree remove ~/Projects/nulled/frappe-api-worktrees/FAPI-12
git -C ~/Projects/nulled/frappe-api fetch --prune
```

Drop the ticket database: `DROP DATABASE IF EXISTS frappe_fapi_12;` in the reused Postgres container. Remote branches are auto-deleted on merge.
