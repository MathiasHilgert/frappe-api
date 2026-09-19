# FAPI-33: Keep Plane docs and skills in sync with every ticket

## Objective

`plane.sh` gains page commands (`pages`, `page-get`, `page-put --dry-run`); the
`working-on-tickets` skill gains a documentation step and a new reference
`keeping-docs-in-sync.md`; the PR template gains a docs/skills checklist item.

## Scope

- `scripts/plane.sh` (actually `.agents/skills/working-on-tickets/scripts/plane.sh`):
  add `pages`, `page-get <name|id> [--out F]`, `page-put <name|id> --html-file F [--dry-run]`.
- `.agents/skills/working-on-tickets/SKILL.md`: add documentation step to checklist.
- `.agents/skills/working-on-tickets/references/keeping-docs-in-sync.md`: new reference.
- `.agents/skills/working-on-tickets/references/reviewing.md`: reviewer checklist item.
- `.github/pull_request_template.md`: "Docs and skills updated" checklist item.
- `.agents/skills/writing-code/references/clean-code.md` (or SKILL.md hard rules):
  two standing rules — prefer maintained libraries; libraries live in infra
  adapters, modules depend on our kernel ports (ArchUnit verified).

## Out of scope

Rewriting existing Plane page content (done by orchestrator after this wave).

## TDD mode

No Java code touched; this is a bash script + Markdown ticket. No existing
automated test harness for `plane.sh` (bash, no test framework in repo).
Verification = `shellcheck` (deterministic static check, acts as our RED/GREEN
signal: command fails to parse/lint before written, passes after) + read-only
exercise against the live Plane API + `--dry-run` for `page-put` (never a real
PUT during this ticket) + `./gradlew check -x test` to confirm no Java/docs
regression.

## Tasks

- [x] 1. Read `plane.sh`, `working-on-tickets` skill + all references, PR template.
- [x] 2. Add `pages` (list) command to `plane.sh`.
- [x] 3. Add `page-get <name|id> [--out F]` command (writes HTML to file).
- [x] 4. Add `page-put <name|id> --html-file F [--dry-run]`: minify HTML, reject
      whitespace between tags, reject tables whose th/td colwidths per row don't
      sum to ~1000; PUT (never PATCH) unless `--dry-run`.
- [x] 5. shellcheck clean; exercise `pages`, `page-get` read-only; `page-put --dry-run`.
- [x] 6. Update `working-on-tickets/SKILL.md` checklist with docs step.
- [x] 7. Add `references/keeping-docs-in-sync.md`.
- [x] 8. Update `references/reviewing.md` checklist item.
- [x] 9. Update `.github/pull_request_template.md`.
- [x] 10. Update `writing-code` skill with the two standing rules.
- [x] 11. `./gradlew check -x test` green.
- [x] 12. Commit.

## Evidence

See report to caller (this doc mirrors the final commit message and
verification commands run).
