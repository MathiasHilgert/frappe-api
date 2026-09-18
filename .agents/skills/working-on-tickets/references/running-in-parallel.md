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
- Testcontainers reuse: ensure `~/.testcontainers.properties` contains `testcontainers.reuse.enable=true`; containers declare `.withReuse(true)`. Containers survive between runs instead of starting per test run.
- One database per ticket: run tests with `FRAPPE_TEST_DB=frappe_fapi_12` in the FAPI-12 worktree. Each database name gets its own reused Postgres container (see `testing-code` → `integration-tests.md`).
- Gradle cache (`~/.gradle`) is shared; do not set a per-worktree `GRADLE_USER_HOME`.

## Cleanup after merge

```bash
git -C ~/Projects/nulled/frappe-api worktree remove ~/Projects/nulled/frappe-api-worktrees/FAPI-12
git -C ~/Projects/nulled/frappe-api fetch --prune
```

Remove the ticket's reused test container:

```bash
docker ps -q --filter label=org.testcontainers | xargs -r docker inspect \
  --format '{{.Id}} {{.Config.Env}}' | rg 'POSTGRES_DB=frappe_fapi_12' | cut -d' ' -f1 | xargs -r docker rm -f
```

Remote branches are auto-deleted on merge.
