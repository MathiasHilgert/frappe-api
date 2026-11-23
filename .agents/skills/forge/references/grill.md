# Grill

Goal: leave the interview with specs a stranger could implement and a user who has defended every choice.

## Rules
- Module planning: ask every decision of the module before go, never mid-run.
- One decision per question, with your recommended answer and the reason in one line. Up to four independent questions may go in one `AskUserQuestion` call; a question whose options depend on an earlier answer waits for that answer. Then stop.
- Look before asking: CodeGraph, the frappe-conventions rules (`.agents/skills/frappe-conventions/SKILL.md`), `contracts/openapi/v1.json`, `contracts/events/v1.json`, `Directory.Packages.props`, the design docs repo (`gh api repos/MathiasHilgert/frappe-docs/contents/<path>`, never clone). Ask only what the code and docs cannot answer; when you looked, say what you found.
- Challenge the use case: who needs it, what happens if we do not build it, the smallest version that delivers it, what breaks for existing clients.
- Push back when an answer contradicts the repo's conventions or a design doc; cite `file:line`.
- Decisions are the user's; facts are yours to verify. Never ask the user to confirm a fact you can check.
- **Challenge.** Before going deep, judge the idea itself: what it costs, what it breaks or complicates in existing behavior, whether a smaller or different change gets the same outcome, whether it is worth doing at all. When it is not, say it plainly with a decision card ("no lo haría, porque…" with the alternative). The user decides; once they insist, build what they asked, well.

## 1. Classify the change first (decides which blocks are mandatory)
Infer it from the request and confirm in one question only if unclear.

| Kind | Examples |
|---|---|
| `docs` | docs, README, ADR text, comments only |
| `tests` | tests only, no production code |
| `small` | one understood fix, no new contract, no schema change |
| `feature` | new behavior in a module |
| `contract` | adds/changes/removes a route, payload, error code, event or option |
| `data` | new table/column/index, migration, retention, backfill |
| `cross-module` | one module needs another's data or reacts to it |
| `security` | auth, permissions, grants, secrets, anything anonymous |
| `operational` | config, pools, deploy, Worker jobs, performance work |

A change can have several kinds; ask every block they trigger.

## 2. Mandatory blocks
| Block | Mandatory for | Ask (each one only if the code does not already answer it) |
|---|---|---|
| Scope | all | Problem and actor; outcome in one sentence; non-goals; acceptance examples (request → answer) |
| Docs accuracy | `docs` | Audience; which behavior the text must match (verify it in code); what to remove |
| Hexagonal layers | every kind but `docs`/`tests` | Which layer owns each rule: domain (pure rules, no HTTP, no EF, no status codes), application (use cases, ports), infrastructure (adapters), Http (status codes, routes). Which existing port or use case is reused; what new port is needed and why |
| Module communication | `cross-module`, `feature` touching two modules | Integration event (async, durable outbox/inbox), a `*.Contracts` query port (sync, ADR 0002/0003), or a local projection/view fed by events? Consistency window the user accepts. Which existing events can we subscribe to (read `contracts/events/v1.json`)? Which events must we emit, with what payload and durability? |
| Data design | `data`, `feature` that stores | Entities and ownership; normal form and every deliberate denormalization (why); keys (UUIDv7), uniqueness and constraints; lifecycle (status + `disabled_at`, no naive soft delete) and retention; expected row counts and growth; queries per operation and the index each needs; migration of existing rows and rollout order |
| Contract and DX | `contract` | Routes in the repo's style (context segment, `/me`, cursor paging, `code`+`name`, Problem Details codes); run `critic-dx` on the proposal before writing specs; breaking or additive; OpenAPI + events contract + Bruno updates |
| Performance | `data`, `operational`, any hot path | Round trips per request, caching and its eviction, transactions without I/O, pools, behavior at 10× and 100× the data |
| Security | `security`, any new route | Who may call it; 404 vs 403; what must not leak (responses, logs, metrics, events); grants |
| Observability | `feature`, `operational`, `cross-module`, `data` with jobs | Technical metrics (latency, outcomes, sizes, ages, failures) and business metrics (what a product owner would chart); when they are recorded (after commit); tags bounded; spans; logs; what an operator alerts on; which existing signal is missing today |
| Impact | every kind but `docs` | Which existing use cases, consumers, jobs and stored data change behavior; what could break or reach an unexpected state; what existing code is reused (CodeGraph callers/impact answers most of it: ask only what it cannot) |
| Failure | `feature`, `cross-module`, `operational` | Partial failure, retries, duplicates, concurrent requests, redelivery, what the user sees |
| Craft | every kind but `docs` | Is there a well-known library already in `Directory.Packages.props` or widely used by the .NET community for this; what senior idiomatic C# for this repo looks like (existing patterns to copy); what is likely to change (vendor, rule, country, format) and where a seam pays off: propose the fitting pattern from `.agents/skills/frappe-conventions/rules/design.md` |

Never ask a block that is not mandatory for the kinds in play (no metrics questions for a docs change).

## Exit
Stop when every mandatory block has an answer or an explicit "not in this change". Summarize decisions in a table (block → decision) and ask for go. The kinds you classified travel with each task as `kinds` (`references/plan.md`): they decide which critics are mandatory.
