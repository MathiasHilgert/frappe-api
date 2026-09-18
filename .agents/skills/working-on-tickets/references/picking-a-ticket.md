# Picking a ticket

## Read

1. `scripts/plane.sh show FAPI-N` — read Scope, Out of scope, Acceptance, Invariants, Contracts, Dependencies.
2. `scripts/plane.sh list in-progress` — tickets already running.
3. If `Dependencies` names a blocker that is not Done, stop and tell the human.

## Parallel or not

Run a ticket in parallel with the in-progress ones only when **both** hold:

- It belongs to a different module (module label / Plane module).
- No blocks / blocked-by relation links it to any in-progress ticket.

Otherwise queue it and say why. `platform` tickets touch shared build files: run them alone.

## Size

Use the `size:*` label; if missing or clearly wrong, classify by expected change:

| Size | Signal |
| --- | --- |
| S | One layer, one use case, < ~150 changed lines |
| M | Several layers of one module, one migration, ~150–400 lines |
| L | New aggregate or module, cross-module events, > ~400 lines (consider splitting) |

## Sensitive areas

Mark the ticket sensitive when it touches any of:

- Authentication or sessions (tokens, PINs, hashing).
- Tenancy: `tenant_id`, RLS policies, schema grants.
- Money: `Money`, rounding, currency, payments, cash.
- Events: outbox, relay, NATS subjects, inbox/idempotency.

Sensitive raises model tier (see `choosing-models.md`) and requires a test that proves the invariant.
