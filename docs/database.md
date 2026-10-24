# Database

## Schemas and migrations

Every module owns one Postgres schema named like the module (`identity`) and keeps its EF migrations history in it.
Wolverine's envelope storage lives in `wolverine`, Quartz in `quartz`. A mapping never points at another module's
schema or type: `ModuleSchemaTests` fails when one does.

```sh
just migration Identity <Name>   # add a migration to a module's Persistence/Migrations
just migrate                     # build, then apply every module's migrations locally (Development)
dotnet ef migrations bundle -p … -s src/Host/Frappe.Api -c IdentityDbContext   # ship and run before the app starts
```

## Connection pools

One instance opens connections from three pools, all to `ConnectionStrings__frappe`:

| Pool | Who | Size it for |
| --- | --- | --- |
| App (Npgsql, one per connection string) | every module DbContext and read port | concurrent requests and handlers in flight |
| Wolverine | outbox, durable inbox, node agents, leader election | a handful: listeners plus the durability agent |
| Quartz | clustered job store | one or two: it polls and fires rarely |

Npgsql defaults to `Maximum Pool Size=100` per pool, which is too many once instances multiply. Start from
`Maximum Pool Size=20` on the app connection string and size the database for
`instances × (app + Wolverine + Quartz) + migrations + admin` below Postgres `max_connections` (default 100). Raise
the app pool only when requests wait on connections (the `db.client.connections.*` metrics in Grafana show it).

### PgBouncer

When instances outgrow `max_connections`, put PgBouncer in front of the app pool in **transaction** mode:

- Add `No Reset On Close=true` to that connection string. Npgsql's automatic prepared statements need PgBouncer
  1.21+ with `max_prepared_statements` set; on older PgBouncer add `Max Auto Prepare=0`.
- Keep Wolverine on a direct (or session-mode) connection: it holds advisory locks for leader election and node
  ownership, which a transaction-mode pooler would hand to another client. The same goes for `resources setup` and
  migrations (DDL, advisory locks).
- Quartz's clustered store locks rows inside its own transactions and works through transaction mode.

Today all three share one connection string; splitting Wolverine onto its own string is the change PgBouncer
requires.

## Per-module roles (planned)

Goal: each module connects as a role with rights on its own schema (and the shared outbox) only, so a bug or an
injection in one module cannot read another's tables. `deploy/postgres/module-roles.sql` is the ready plan: one
`NOLOGIN` role per module, `USAGE` and DML on its schema, default privileges for tables migrations create later,
and DML on `wolverine`. `ModuleRoleTests` runs that script against an empty database and proves the identity role
reads `identity` and is refused another schema, so the plan stays correct as it grows.

Not switched on yet, because it would cost every developer a reset of their local database volume (roles and
ownership are set when a schema is created) while there is one business module. Rollout, when a second module
with data lands:

1. Run the script (extended with a block per module) as the database owner in each environment.
2. Give each module its own connection string (`ConnectionStrings__identity`, ...) logging in as a user that is a
   member of its role; `AddModuleDbContext` picks the module's string when present, the shared one otherwise.
3. Run migrations as the owner, so new tables inherit the default privileges.
4. Add an integration test per module that its connection is refused another module's schema.
