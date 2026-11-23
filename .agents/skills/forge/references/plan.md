# Plan

## ODD document (`odd/tasks/<feature>.md`, git-ignored)
Order: two-line header (objective, branches, runner); `## Specs` with numbered `S#` quoting the user's strings and examples verbatim; `## Tasks` one
line each (ID, `S#`, wave, worktree, profile, contract, qa, status, commit); `## Log` with `L1` = the user's request verbatim, then decisions verbatim
with dates.

## Impact (before the design review)
Write an `## Impact` section in the ODD document from CodeGraph (`callers`, `impact` of every symbol the change touches) and the existing use cases:
which use cases, consumers, jobs and stored data are affected; what changes in **behavior** (not only contracts) for each; states the change can
create that nothing expects; incompatibilities with rows already stored; what to reuse (existing ports, helpers, value objects) and where reusing
would mix layers. If the impact or the value is poor, say so to the user before planning (`references/grill.md`, Challenge).

## Design review (before specs are final)
Design critics run on `model: opus`, `effort: high` whatever their frontmatter says: a design mistake costs a whole wave, and there are at most three
of them. Implementation roles keep their frontmatter (the table in `references/verification.md` is the source of truth).
Write the proposed design in the document (layers per rule, module communication, schema, routes, events, metrics, and the patterns chosen with the
symptom each answers — `.agents/skills/frappe-conventions/rules/design.md`) and launch, in parallel, only the design critics the kinds call for, each
with "review this proposed design, there is no diff yet":
- `critic-dx` when routes or payloads change;
- `critic-architecture` for any code change (layers, module communication, libraries);
- `critic-data` when the schema changes.
Fix the design from their findings, then show the plan.
Roles registered in `.claude/agents/` may load only after a session restarts; launch a design critic as `general-purpose` with "Your role: read
`<repo>/.agents/skills/forge/agents/<role>.md` (ignore its frontmatter) and follow it". The Workflow does the same fallback on its own.

## Tasks
- Every task carries a `brief` written at launch (never edited before its resume: it is part of every cache key). Orientation before the first edit is
  a large share of writer time, so the brief answers what the writer would otherwise explore. 5-15 lines, in this order:
  - **Specs**: the `S#` of this task and the user's choices verbatim.
  - **Files**: the exact paths to change or create, each with one line on what changes.
  - **Reuse**: the existing symbols to call (type and method, with path), and the sibling to copy the shape from.
  - **Tests**: the test project, the test class to create or extend, and the command `test-class.sh <Class>`.
  - **Scaffold**: the `just scaffold <kind>` recipe to run first, when one fits.
  - **Carry-overs**: decisions from earlier tasks this one must keep.
  Find the facts with CodeGraph while planning; a brief that says "explore X" instead of naming it is not done.
- Before writing by hand, a writer runs `just scaffold <kind>` for the repetitive skeleton (use case, error with its three messages, integration
  event, endpoint, Bruno request) once that recipe exists on main.
- One task = one independent behavior with its tests and docs. Never split tests, docs and code into different tasks.
- **Contract rule:** a task that adds, changes or removes a route, payload field, error code, event or option has `contract: true`; its specs include
  "OpenAPI and events contracts regenerated (`just contracts update`) and `tools/bruno/` updated for every added, changed or removed endpoint".
- Edit surfaces: exact repo-relative paths or narrow globs; include `contracts/**` and `tools/bruno/**` when the contract rule applies.
- Waves: tasks in one wave have disjoint surfaces; a task that needs another's code goes in a later wave. Migrations of the same module never share a
  wave.

## Plan per module
Plan a whole module (or every feature still missing in it) in one pass: one grill with every decision asked upfront and in one batch, one schema
design, one design review, one feature document. No questions mid-run: anything the plan cannot decide becomes a decision card before go.

## Chains
- Merges stay squash with the PR body as the commit body so `git log` and `git blame` reach the why.
- The module is cut into **slices** before go. A slice is one capability with its code, tests and docs, about **800-1500 authored lines**, that leaves
  main working; its `commit` header is its future PR title. The cuts are written in the feature document and shown in the briefing.
- Workflow args: `"chains": [[slice, slice, ...]]`. Every slice of a chain shares one `worktree` and one `branch` (`<type>/<module-topic>`); each
  slice adds `"commit": "<type>(<scope>): <summary>"`.
- Per slice: the writer builds and commits it; critics review that commit while the next writer starts; the correction is a `fixup!` commit; QA runs
  once on the head. A blocker that stops slice k stops the chain after the slice already being written.
- Two chains that touch different modules run in parallel (`chains: [[...], [...]]`); never two chains on the same module.
- Profile per slice by risk: `deep` for schema changes on existing rows, security or contract breaks; `standard` for features; `small` for internal
  logic with good tests. Do not give every slice `deep`.

## PR budget (planned before go, shown in the briefing)
- Chains: one PR per slice (800-1500 authored lines). Below, the budget for a single task outside a chain.
- Budget: about **400 authored changed lines per PR** (additions + deletions), not counting generated files (EF `*.Designer.cs`, model snapshots,
  `packages.lock.json`, `contracts/**` regenerated contracts). A planning heuristic, never a hard gate: a behavior that is clearer whole may exceed
  it, say why.
- Forecast the authored lines per task. Under budget: one PR per task (or one PR for the feature when the tasks are small). Over budget: slice into a
  chain of PRs, each one reviewable alone, with its tests and docs, in dependency order.
- Chain strategy (ask once with a decision card, save it in `forge/<feature>/decisions`): `stacked-to-main` (each PR targets the previous branch,
  merged in order into main) or `feature-branch-chain` (PRs target a feature branch merged to main at the end). Load the `chained-pr` skill before
  creating the chain.
- A task whose risk is high (data migration of existing rows, security, contract break) gets its own PR so it can be reviewed and reverted alone.

## Profile per task (decides depth; the Workflow also checks the real diff)
| Profile | When | Critics | QA |
|---|---|---|---|
| `docs` | only docs/comments | none, or `contracts` + `dx` if `contracts/**` or `tools/bruno/**` changed | no |
| `tests` | only tests | `tests` | no |
| `mechanical` | convention or style alignment with no behavior change; run `scripts/codemods/run.sh <worktree> <folders>` first and set `codemods: true`; split a large area by folder into parallel tasks; writer effort `low` | `correctness` + `craft` (cap 2) | no |
| `small` | understood fix, no contract, no schema | selector, cap 3 | only if an endpoint's behavior changed |
| `standard` | feature work | selector, cap 5 | if runtime behavior changed |
| `deep` | security, data migration of existing rows, cross-module, or the user asked | selector, cap 6 | yes |

The selector (in `assets/forge.workflow.js`, tested by `node scripts/review/selector.test.mjs`) works in three layers:
1. **Plan:** each task carries `kinds` from the grill. `security` → security; `data` → data + performance; `contract` → contracts + dx; `cross-module`
   → architecture + contracts; `operational` → performance + observability. These and correctness are mandatory: the cap never drops them.
2. **Content:** the writer's last step runs `scripts/review/signals.py`, which scans the changed lines (authorization calls, migrations, locks and
   transactions, routes, metrics and logs, removed assertions…) and returns evidence per lens. A lens with content evidence outranks one triggered
   only by a path.
3. **Data:** `lensStats` (from Engram `forge/metrics/critics`) demotes a lens with 30+ findings of which the arbiter kept under 20 %: it runs only
   when the cap leaves room. Never a mandatory one.
Optional lenses fill the cap by content first, then path, undemoted before demoted, then this priority: correctness, security, data, contracts, tests,
performance, dx, observability, architecture, craft (style last). Any changed test file brings the tests critic. Every task's result carries
`selection` with the reason each critic ran or was dropped. The Workflow never runs more than the profile allows and downgrades automatically when the
diff turns out to be docs- or tests-only; it logs every skipped critic or QA with the reason.

## Workflow args
```json
{
  "repo": "<repo>",
  "feature": "<feature>",
  "featureDoc": "<repo>/odd/tasks/<feature>.md",
  "base": "origin/main",
  "answers": {},
  "apply": {},
  "chains": [[{ "id": "S1", "title": "...", "specs": "S1-S3", "worktree": "<wt>", "branch": "feat/<module-topic>", "commit": "feat(<scope>): <summary>", "surfaces": ["..."], "profile": "standard", "kinds": ["feature", "contract"], "contract": true, "qa": true }]],
  "waves": [[{
    "id": "T1", "title": "...", "specs": "S1-S2",
    "worktree": "<worktrees>/<slug>",
    "branch": "feat/<slug>",
    "surfaces": ["src/Modules/X/**", "tests/Modules/X/**", "contracts/**", "tools/bruno/x/**"],
    "profile": "standard", "kinds": ["feature", "contract"], "contract": true, "qa": true
  }]],
  "lensStats": { "craft": { "findings": 42, "blockers": 0, "advisories": 6 } },
  "deferQa": true,
  "suppressions": [{ "lens": "craft", "rule": "No requests for XML docs on private members", "reason": "declined by the user" }]
}
```
Every task gets `kinds`: the grill's change kinds that apply to THAT task, not the whole feature (a task with no migration is not `data`). `lensStats`
is the current total from Engram `forge/metrics/critics` and `suppressions` the list in `forge/review/suppressions`; omit either when empty. Pass
`args` as a JSON object, not a string. Create `odd/tasks/<feature>.journal/` before launching. To pass the user's choices back, add `"answers": {"T1":
{"D1": "<chosen label> — <user's words verbatim>"}}` and relaunch with `resumeFromRunId`.
