---
name: working-on-tickets
description: "Orchestrates a Plane ticket end to end: pick, route models, worktree, implement, review, open PR, sync Plane state. Use when starting, parallelizing, reviewing, shipping or merging FAPI tickets."
license: Proprietary
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract

Load when asked to work on, pick up, parallelize, review, ship, merge or create a Plane ticket (`FAPI-N`), or to act on PR feedback for one.

## Hard Rules

- Use `scripts/plane.sh` for every Plane read or write; never hand-roll API calls. `PLANE_API_KEY` stays in the environment.
- One ticket = one worktree = one branch `<type>/fapi-N-<slug>` = one PR.
- The orchestrator delegates code to a writer subagent and review to reviewer subagents; it never writes feature code itself.
- Never merge without the human saying "merge". Merges are squash-only.
- No AI attribution in commits, PRs or Plane comments.

## Decision Gates

| Situation | Reference |
| --- | --- |
| Which ticket, can it run in parallel, size and sensitivity | `references/picking-a-ticket.md` |
| Which model for writer, reviewers, fixer | `references/choosing-models.md` |
| State transitions, creating tickets | `references/keeping-plane-in-sync.md` |
| Worktrees, databases, Docker, cleanup | `references/running-in-parallel.md` |
| Reviewer brief and findings | `references/reviewing.md` |
| Commits, PR, CI, report, merge, feedback | `references/shipping-a-pr.md` |
| What doc/skill change goes where, fact-checking before publishing | `references/keeping-docs-in-sync.md` |

## Execution Steps

Copy this checklist and tick it as you go:

```
- [ ] 1. plane.sh show FAPI-N; classify size and sensitive areas
- [ ] 2. Parallelism check against plane.sh list in-progress
- [ ] 3. Create worktree + branch; plane.sh move FAPI-N in-progress
- [ ] 4. Spawn writer (model per table) with ticket text, skills writing-code + testing-code
- [ ] 5. Writer reports ./gradlew check green and RED/GREEN evidence; skill references updated in the same PR if a convention changed
- [ ] 6. Spawn reviewer(s); fixer applies confirmed findings only
- [ ] 7. Push, open PR per template incl. "Docs and skills updated"; gh pr checks --watch (max 3 fix attempts)
- [ ] 8. plane.sh move FAPI-N in-review; report to human
- [ ] 9. On "merge": squash merge, move done, remove worktree
- [ ] 10. After merge: record decisions in the Decision Log; sync Architecture / Engineering Standards / Roadmap, fact-checked against main (`references/keeping-docs-in-sync.md`)
```

## Output Contract

Report to the human: ticket key and title, PR URL, checks table (name → result), reviewer verdict, decisions made, open questions. Nothing else.

## References

- `references/*.md` — one file per row of Decision Gates; `scripts/plane.sh` (run `plane.sh --help`).
