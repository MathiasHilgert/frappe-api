# Reviewing

Reviews run in the worktree **before** the PR is opened.

## Reviewer brief (pass verbatim, fill the blanks)

```
Review ticket FAPI-N in <worktree path>. Diff: git diff origin/main...HEAD.
Ticket text: <plane.sh show output>.
Load skills writing-code and testing-code; judge against them.
Do not edit files. Return findings only.
```

With two reviewers, send each the same brief separately; neither sees the other's output (blind).

## Checklist

- Acceptance criteria each covered by a test; RED → GREEN evidence present.
- Domain has no Spring/JPA imports; expected failures return `Result`.
- No cross-module access to internals or tables; events go through the outbox.
- Tenant-scoped tables have `tenant_id` + RLS policy + a test proving isolation.
- Money uses `Money`; time uses the injected `Clock`.
- HTTP errors are ProblemDetail; endpoints under `/v1`.
- Migrations are forward-only Flyway scripts in the module schema.
- Testcontainers, never H2. `./gradlew check` green.
- Change stays within ticket scope.
- Skills and docs reflect the change (`references/keeping-docs-in-sync.md`).
- The PR's Observability table lists every metric and span the diff adds (name, type, unit, tags with allowed values), and its Events table lists every event published or consumed (version, publisher, consumers, subject, payload, idempotency key); both match the code.

## Findings format

| Severity | Meaning |
| --- | --- |
| blocker | Wrong behavior, broken invariant, security/tenancy leak, failing check |
| major | Standard violation or missing test for an acceptance criterion |
| minor | Naming, readability, small duplication |

Each finding: `severity | file:line | problem | suggested fix`.

## After review

- Orchestrator merges both reviewer lists; a finding is **confirmed** when it is a blocker/major that the orchestrator verifies in the code, or both reviewers report it.
- One fixer (writer tier) applies only confirmed findings, reruns `./gradlew check`, reports.
- Minor findings: fix only if trivial; otherwise list them in the PR under Pending decisions.
