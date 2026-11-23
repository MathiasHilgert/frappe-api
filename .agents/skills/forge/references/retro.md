# Lessons and the retro

Access, topics and rules for every stage: `references/memory.md`. This file covers recall of lessons and the retro.

## Recall (step 0, before the grill)

- Search `forge/lessons` and the feature's topic words; read every hit tagged `forge-retro` in full.
- Apply what they say (a command that needs a flag, a test fixture quirk, a check CI runs) to the plan and to the writers' prompts (pass the relevant
  lessons in the task specs or the journal).

## Retro (last step, after delivery or after the user stops the feature)

Gather the friction:

- the task journals (`odd/tasks/<feature>.journal/`);
- each writer's and QA's `friction[]` (problem, attempts, how it was solved) from the Workflow result;
- the orchestrator's own: CI failures after push, rebase conflicts, decisions that came back late, agents that failed or returned nothing.

For each lesson, decide:

| Lesson | Action |
|---|---|
| Caused by forge itself (a prompt, the workflow, a script, a missing check) | Propose the fix to the user with `AskUserQuestion` (recommended: fix it now); if accepted, fix it in `.agents/skills/forge/` in this session |
| A repo or toolchain fact future runs must know (a command that fails, a quirk, a convention) | Save to Engram |
| One-off, will not recur | Drop it |

Saving: search first (`forge/lessons <keywords>`); update the existing lesson if one covers it, else save a new one. Format: title `forge-retro: <the
problem in a few words>`; content with **What** (the problem), **Why** (root cause), **Fix** (the command, flag or step that works), **Where** (files
or tools), tag line `tags: forge-retro, forge/lessons, <area>`; topic key `forge/lessons/<area>` (`toolchain`, `tests`, `migrations`, `ci`, `docker`,
`workflow`…); type `discovery` (or `bugfix` when it was a defect). Never save secrets or personal data.

## Outcome metric

Run `python3 -I <repo>/.agents/skills/forge/scripts/metrics/followup_fixes.py --repo <repo> --branch origin/main --window 60` and save the JSON with
its date to Engram `forge/metrics/outcomes` (update the topic, keep the history as lines). `rate` counts changes a later commit names in a `Fixes:`
trailer; `overlap_rate` is a rough upper bound from shared files. Report both to the user next to the run's cost; a change to forge's review (critics,
models, thresholds) is judged by whether `rate` goes down, not by how many findings it produces.

## Time

Aggregate `scripts/metrics/run_timings.py` over the feature's runs: wall minutes per phase and the slowest agents. Save the totals to Engram
`forge/metrics/timings`. The writer's long tail is the time to cut, not the review.

## Critic metrics

Every Workflow result carries `criticStats`: per critic, runs, silent runs, findings, and how many the arbiter kept as blockers or advisories or
dropped. Add this run's numbers to the Engram topic `forge/metrics/critics` (search it, update the totals, keep the date range), stored as a JSON line
`{"<lens>": {"findings": n, "blockers": n, "advisories": n}}` so the next launch passes it as `lensStats` unchanged; the Workflow demotes noisy lenses
from it on its own. Once a critic has 30+ findings, judge it: one whose findings the arbiter drops most of the time, or that never produced a blocker,
is a candidate to move later in the priority or to a cheaper model; propose it to the user as a decision card with the numbers. Never prune a critic
on fewer runs.

Report to the user: lessons saved (titles), forge fixes applied or declined, and anything dropped (one line).
