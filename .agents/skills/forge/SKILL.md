---
name: forge
description: "Trigger: /forge, forge this, build this feature end to end. Grill the user, plan with ODD, then run writers, critics, arbiter and QA in parallel worktrees."
license: Apache-2.0
metadata:
  author: "MathiasHilgert"
  version: "2.3"
---

## Activation Contract

Load when the user types `/forge` or asks to take a change from idea to merged PR in this repo. The session becomes the orchestrator; it writes no
product code itself.

## Hard Rules

1. Classify the change and pick its shape; grill only the mandatory blocks its kinds trigger and its shape allows, one question at a time, never
   what the code answers (`references/grill.md`).
2. Think in the modular monolith: each rule in its hexagonal layer, modules talking only through events, contracts ports or projections.
3. No writer before verbatim specs, the design review its shape calls for and the user's go.
4. Design review by shape (`references/plan.md`): none for `fix` and `refactor`; `critic-dx` for a `crud` with new routes; `dx`, `architecture`,
   `data` and `security` for a module. Critical tasks add the attacker pass's question on where each secret or personal datum rests.
5. A public contract change carries its docs: `just contracts update` and `tools/bruno/`.
6. Depth scales with the change: the shape (`fix` | `refactor` | `crud`, plus `docs` and `tests`) decides the process and the risk (`standard` |
   `critical`) decides review depth (`references/plan.md`, Shape and risk per task). Never run critics, an arbiter or QA the Workflow does not call
   for.
7. Implementation, review and QA run only through `assets/forge.workflow.js`.
8. The orchestrator owns git (worktrees, PRs, merges). Only exception: in a chain each writer commits its own slice, and corrections land as `fixup!`
   commits that the orchestrator folds in before opening PRs.
9. CodeGraph for every structural question, initialized in each worktree.
10. You are the only one who talks to the user: each agent decision request becomes one `AskUserQuestion` (`references/communication.md`); never
    forward raw reports.
11. Engram at every stage, project `frappe-api` only (`references/memory.md`); MCP, else CLI, else ask.
12. Show the TL briefing (`references/briefing.md`) before asking for go.
13. Verify fast (`references/verification.md`): filtered tests while writing, one parallel final gate; never idle on CI, prepare the next PR
    meanwhile. Challenge the request when its value or impact is poor; the user decides.
14. Code rules live in frappe-conventions (`.agents/skills/frappe-conventions/SKILL.md`), never here: agents load only the rule files their task or
    diff needs and cite rule ids. Surviving convention proposals (`conventions[]` in the Workflow result) reach the user as decision cards: keep the
    rule, add an exception (marked in code), or change the rule (you edit the card).

## Decision Gates

| Situation | Action |
|---|---|
| A module, or a feature over one PR | A plan, not a shape: module document, design review (`dx`, `architecture`, `data`, `security`), a foundation chain with its writer on Opus, then slices that each carry their own shape, one stacked PR per slice and QA once on the head. Plan the whole module at once (one grill, one schema, every decision upfront). Cut it into a short foundation chain, then feature chains in parallel worktrees branched from its head (`references/plan.md`, Foundation, then fan-out). Each runs as a **chain**: one branch, one commit per planned slice, critics on slice k while slice k+1 is written, QA once on the head (`references/plan.md`, Chains). Independent modules run as parallel chains |
| Shape `docs` or `tests` | Skip design review; one task; no QA |
| Shape `fix` (an understood bug, no contract change) | Zero or one question; no design review; the writer starts from a failing regression test; `correctness` only, arbiter only on findings; QA only if an endpoint changed; PR under 200 lines |
| Shape `refactor` (same behavior, better code) | Brief grill (Scope, Layers); no design review; run `scripts/codemods/run.sh <worktree> <folders>` first when mechanical and set `codemods: true`; split big areas by folder; `craft` + `architecture`, arbiter only on findings; no QA; PR under 500 lines per folder. A diff under `contracts/**` or a removed assertion makes it a `crud` |
| Shape `crud` (a use case, entity or route) | Brief grill (Scope, Layers, Contract); `critic-dx` only for new routes; writer starts from `just scaffold crud <Module> <Entity>`; `correctness` + `contracts` and the arbiter; QA a Bruno smoke; PR under 500 lines of `src/` |
| Critical (kinds `security`, `money`, `data-migration`, `cross-module`, or `risk: critical`) | On top of the shape: `security` and `data` critics mandatory; the attacker pass asks where each secret or personal datum rests, who reads it and for how long; QA always; writer on Opus at effort `high`; about 20 tests per use case plus property tests on parsers and money |
| Workflow returns `needs_input` | One `AskUserQuestion` per blocking decision. A decision whose `source` is `writer` goes in `answers[taskId][decisionId]` (the writer reruns); one whose `source` is `arbiter` or `qa` goes in `rulings` (a `fixup!`, nothing reruns). Relaunch FRESH from `odd/tasks/<feature>.args.json` plus `done` from `tasks[].done`, never by retyping the args or with `resumeFromRunId` on a chain (`references/plan.md`, Save the args). Static per-task context goes in `task.brief` at launch, never in `answers` |
| Planning waves | Waves are for small independent tasks only. Tasks that change the same aggregate or module go in one chain, never in parallel branches |
| Workflow `done` with advisories worth applying | The arbiter marks cheap advisories `autoApply` and the correction round already applied them. Only the rest reach the user: one decision card; on yes, for waves relaunch with `resumeFromRunId`, the SAME `answers` and task `brief`, and `apply[taskId]` holding advisory ids or titles (titles survive renumbering); for chains relaunch fresh with `done` and each accepted advisory as a `rulings` entry of its slice (title, evidence and fix as the answer). Never put advisory text in `answers` or `brief`: they are part of every cache key, and changing them reruns writer, critics and QA |
| Workflow returns `needs_decision` | Turn open blockers or QA defects into decision requests and ask them the same way |
| Speed over depth (user asked) | Review per PR: run `mode: "write"`, open the PRs, then `mode: "review"` with each task's `changedPaths`, `pr` and `signals` (run `scripts/review/signals.py --worktree <wt> --base origin/main` yourself) while CI runs; fix blockers only; advisories go to one final sweep |
| All tasks `done` | Deliver per `references/delivery.md`: with `deferQa: true` (the default launch), open the PRs first and run `mode: "qa"` on the `qaPending` entries while CI runs |
| After every Workflow run | Measure it: `python3 -I scripts/metrics/run_timings.py <transcript dir of the run>` (or `--latest ~/.claude/projects/<project>`) and add the phases' minutes and the slowest agents to the ODD Log; the retro aggregates them |

## Execution Steps

0. **Recall** from Engram (`references/memory.md`): lessons, and the feature's mirror and decisions when resuming.
1. **Classify and grill** (`references/grill.md`) until every mandatory block is answered.
2. **Design review and plan** (`references/plan.md`): write `odd/tasks/<feature>.md`, run the design critics, fix the design, set each task's shape,
   risk, contract and QA flags, split into waves and PRs within the budget; show the TL briefing (`references/briefing.md`); wait for go.
3. **Prepare**: per chain (or per wave task) `git worktree add ../frappe-api-worktrees/<slug> -b <type>/<slug> origin/main`, then `gentle-ai codegraph
   init --cwd <worktree>`. Once per clone, install `scripts/hooks/commit-msg` as `$(git rev-parse --git-common-dir)/hooks/commit-msg`:
   subagents get an attribution instruction from the harness, and CI rejects those trailers.
4. **Launch** `Workflow({ scriptPath: "<repo>/.agents/skills/forge/assets/forge.workflow.js", args })` with `deferQa: true`, `kinds` on every task,
   and `lensStats` and `suppressions` from Engram (`references/plan.md`). Critics review blind (specs and code, never the writer's story), list what
   they ruled out, and a critic that fails is retried once and never counts as zero findings. End the turn; resume on its notification. Roles you
   launch yourself (design review) use their definition files (`references/plan.md`).
5. **Read** the result; update the ODD document per task (shape, risk, critics run and skipped, verdict, fix review, QA); keep `criticStats` for the
   retro; relay questions or decisions.
6. **Deliver** (`references/delivery.md`).
7. **Retro** (`references/retro.md`): gather friction, fix forge or save lessons to Engram.
8. **Report** (`references/report.md`): the story of what was built, in the chat (a page only when asked).

## Output Contract

After each phase, report in the user's language: what was decided or produced, the ODD document path, what was skipped and why, open question (one),
next step. After delivery: PRs, merges, follow-ups.

## References

- `references/grill.md` — change kinds and mandatory question blocks
- `references/plan.md` — design review, shapes and risk, waves, workflow args
- `references/delivery.md` — commits, PRs, CI, merge, cleanup
- `references/briefing.md` — the TL briefing shown before go
- `references/report.md` — the final report: story, pieces, data, events, PRs
- `.agents/skills/frappe-conventions/SKILL.md` — code conventions index (rules per area, design patterns in `rules/design.md`)
- `references/verification.md` — fast build, test and gate policy
- `references/pitfalls.md` — known tool pitfalls every writer reads first
- `scripts/review/signals.py` — content signals per lens for the critic selector (tests: `scripts/review/selector.test.mjs`)
- `scripts/metrics/run_timings.py` — minutes, turns and tokens per phase and per agent of one Workflow run (from its transcript)
- `scripts/hooks/writer_guard.py` — forge-writer hook: denies whole-file rewrites of tracked files (Write tool, `>`, `tee`, Python `open(..., 'w')`),
  trims unpiped dotnet/just output (`run_trimmed.sh`); tests: `writer_guard.test.sh`
- `scripts/gate/gate.sh` — the writer's final gate in one call (build once, checks and affected test apps in parallel, `--rerun` only what failed);
  `scripts/gate/test-class.sh` — one test class under a 60 s timeout; `scripts/gate/affected.py` — test apps a change can break
- `scripts/metrics/followup_fixes.py` — outcome metric: share of changes that needed a `Fixes:` follow-up within 60 days
- `scripts/review/workflow.test.mjs` — behavior tests of the review stage against a fake agent
- `scripts/codemods/run.sh` — deterministic fixes run before a mechanical `refactor` writer
- `references/memory.md` — Engram at every stage: access, topics, when to read and write
- `references/retro.md` — lessons recall and the retro
- `references/communication.md` — decision requests, how to ask the user, the task journal
- `references/critic-protocol.md` — what every critic follows
- `agents/` — writer, critics, arbiter, QA definitions
- `scripts/slots/README.md` — isolated Docker slots for QA
