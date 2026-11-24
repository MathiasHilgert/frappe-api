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
- `critic-data` when the schema changes;
- `critic-security` when a task has the kind `security`, as an **attacker pass**. For every route, it answers in a table two questions: "what does
  comparing two answers reveal (enumeration through status, body, id, timing or mail)?" and "what can someone take over with what this answer or the
  mail gives them?".
Fix the design from their findings, then show the plan. A design flaw found by a critic after the code exists costs a stop, a correction and the
slices built on top of it. In identity, the id a repeated registration returned stopped the chain at slice 6 of 11.

**Composition dry run (new module or platform change).** Before launch, check that the design composes: build the Api, Worker and Migrator with
the planned project references (an empty module skeleton is enough) and run `HostReferenceTests` and `LayeringTests`. A closure the tests forbid
(the Migrator reaching the broker or ASP.NET, the Api resolving a Worker-only service) becomes a platform slice or a design fix now, not a
stopped chain later. Missing it cost a stopped chain and a redone slice once.
Roles registered in `.claude/agents/` may load only after a session restarts; launch a design critic as `general-purpose` with "Your role: read
`<repo>/.agents/skills/forge/agents/<role>.md` (ignore its frontmatter) and follow it". The Workflow does the same fallback on its own.

## Tasks

- Every task carries a `brief` written at launch (never edited before its resume: it is part of every cache key). Orientation before the first edit is
  a large share of writer time, so the brief answers what the writer would otherwise explore. 5-15 lines, in this order:
  - **Specs**: the `S#` of this task and the user's choices verbatim.
  - **Read first**: the exact files the writer must read before editing, and nothing else (orientation is the writer's largest fixed cost: briefs
    without this list cost 6-8 minutes per slice).
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
- Edit surfaces: exact repo-relative paths or narrow globs; include `contracts/**` and `tools/bruno/**` when the contract rule applies. Grant the repo
  configuration a slice may have to touch up front, so a config file never stops a chain: `eng/typos.toml` (texts in other languages),
  `deploy/.env.example`, `deploy/compose.yaml`, `eng/just/*.just`, `tests/Frappe.EndToEnd.Tests/DeployedFrappe.cs`, `.editorconfig`,
  `.gitleaks.toml`, and `.github/scripts/*` for a module's first use of a CI step.
- **User-provided designs** (a mockup, an artifact, a design system): extract them to a file before the writer starts, with the assets as files
  (logos, icons), the token values and the copy verbatim, and give the writer that path as the source of truth; never summarize a design in words.
  The writer's evidence includes screenshots of every view in light and dark; the orchestrator compares them with the design before review is
  trusted and shows them to the user before anything real is sent. Critics review code, not looks.
- Waves: tasks in one wave have disjoint surfaces; a task that needs another's code goes in a later wave. Migrations of the same module never share a
  wave.

## Plan per module

Plan a whole module (or every feature still missing in it) in one pass: one grill with every decision asked upfront and in one batch, one schema
design, one design review, one feature document. No questions mid-run: anything the plan cannot decide becomes a decision card before go.

## Chains

- Merges stay squash with the PR body as the commit body so `git log` and `git blame` reach the why.
- The module is cut into **slices** before go. A slice is one capability with its code, tests and docs, about **800-1500 authored lines** and
  **at most 3-4 responsibilities**, that leaves main working; its `commit` header is its future PR title. The cuts are written in the feature
  document and shown in the briefing. A writer generates one file at a time: a slice with 7 responsibilities took 22 minutes of which tools were 5.
  Do not pile a previous slice's carry-overs onto a slice that is already full: give them their own slice.
- A running chain may be stopped to apply better briefs to the slices not yet started, but only after the writer in flight has committed and its
  result is journaled (the next writer has started); stopping mid-write loses the slice.
- Workflow args: `"chains": [[slice, slice, ...]]`. Every slice of a chain shares one `worktree` and one `branch` (`<type>/<module-topic>`); each
  slice adds `"commit": "<type>(<scope>): <summary>"`.
- Per slice: the writer builds and commits it; critics review that commit while the next writer starts; the correction is a `fixup!` commit; QA runs
  once on the head. A blocker that stops slice k stops the chain after the slice already being written.
- Parallel chains come from the real dependency graph, not from guesses: read the `ProjectReference` edges of `src/**/*.csproj` and
  `tests/**/*.csproj` and group the slices by the projects they touch. Slices whose projects do not depend on each other (neither directly nor
  through a shared project that one of them changes) go in separate chains on separate worktrees, each based on the last common slice, and run
  at the same time (`chains: [[...], [...]]`); a slice that references a project another slice changes stays after it in the same chain.
- **Foundation, then fan-out (inside one module).** A chain runs its slices one after another, so its wall time is the sum of them all. Cut a module
  into a short **foundation chain** (the schema, the shared aggregates and ports, and every seam the features share) and **feature slices** that
  depend only on the foundation. Once the foundation passes, give each feature slice its own worktree, branched from the foundation head, and run
  them all as parallel chains with `task.base` set to that head. A feature that needs another feature (for example, an email change that needs
  step-up) stays behind it in the same chain.
  - The route list, the dependency-injection list, the message resources, `contracts/` and Bruno are expected merge points, never a reason to
    serialize. The orchestrator merges the branches in order, keeps both sides of the list conflicts, then runs `just contracts update` once.
  - Watch the machine: each parallel chain runs its own Testcontainers gate. Above three or four parallel chains, the gates fight for the CPU.
- Files every chain edits (`Frappe.slnx`, `Directory.Packages.props`, test `.csproj` files and `packages.lock.json`) are expected merge points,
  not a reason to serialize: the orchestrator resolves them by taking the union of both sides and then runs `dotnet restore --force-evaluate` to
  regenerate the lock files.
- Profile per slice by risk: `deep` for schema changes on existing rows, security or contract breaks; `standard` for features; `small` for internal
  logic with good tests. Do not give every slice `deep`.
- **Freeze the seams in the foundation.** Fan-out only pays when the parallel branches cannot change what they share. The foundation fixes the ports,
  the schema and migrations, the event contracts and the test fixtures. Each shared file (DI registration, `IdentityHttp`-style route lists,
  OpenAPI, migrations) has one owner: the branches only append next to existing lines. After the fan-in, the full suite runs once on the merged head,
  because slices that pass alone can still break when combined.

## Writer effort and context

Most of a writer's time is spent generating tokens (about 100k per slice at roughly 90 tokens per second). Two inputs decide how much it generates,
and both are set by the orchestrator per slice:
- **`effort`** (`low` | `medium` | `high`, default `low`; user decision). Raise it only for a named reason, recorded in the tasks table:
  - `medium` for concurrency, locking or a security rule with an attack in the specs;
  - `high` only for a slice that already failed review at a lower effort.
  Lower effort trims thinking, comments and extra tool calls; the gate and the critics still catch what it misses.
- **`files`** (the assembled context): the exact files, with line ranges when large, that the slice needs to read: the ports and aggregates it
  uses, the closest existing example to copy, and its test fixture. Build it with `codegraph_explore` while planning. The writer reads them in one
  batch and explores beyond them only for an open question. The `brief` keeps the what and the why; `files` replaces "Read first" lists.
The writer also stops when done (`agents/forge-writer.md`, rule 9a): no reviews of its own, no tests, docs or files beyond the specs.

## Review by risk

- Critics are lenses chosen by the slice's kinds and diff, never all of them. A finding needs `file:line`, a failure scenario and a severity, or the
  arbiter drops it; the recheck reads only the correction diff.
- A rule that a test can check belongs in a test, not a lens: architecture in `LayeringTests` and `HostReferenceTests`, conventions in analyzers,
  contracts in the `just contracts update` diff, auth flows in pre-written RED tests (enumeration, lockout, code brute force, email-change takeover)
  added in the foundation. Each lens turned into a test is a lens the selector can drop.

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
  "chains": [[{ "id": "S1", "title": "...", "specs": "S1-S3", "worktree": "<wt>", "branch": "feat/<module-topic>", "commit": "feat(<scope>): <summary>", "surfaces": ["..."], "profile": "standard", "kinds": ["feature", "contract"], "contract": true, "qa": true, "effort": "low", "files": ["src/Modules/X/.../Port.cs", "src/Modules/X/.../Example.cs:20-80"] }]],
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
`args` as a JSON object, not a string. Create `odd/tasks/<feature>.journal/` before launching.

**Save the args, continue fresh.**
- At launch, write the exact args to `odd/tasks/<feature>.args.json`. Every relaunch builds its args from that file (Read), never by retyping them,
  and never from a notification, which HTML-escapes `<`/`>`. One escaped brief changed every cache key after it, and slices already done ran again.
- `resumeFromRunId` only for a run with no chains (waves) where only `apply` changes. A chain starts agents concurrently and the Workflow cache is
  positional, so a resume reruns from the first call whose order moved.
- To continue a chain, launch **fresh** (no `resumeFromRunId`) with the saved args plus `done`: for every slice already committed, the `done` entry
  the last result returned in `tasks[].done` (`{commit, changedPaths, contractChanged, signals, reviewed}`). A done slice never reruns its writer; a
  reviewed one never reruns its critics.
- Where the user's choice goes depends on the `source` of the decision:
  - `writer` (the writer stopped before committing): `"answers": {"T1": {"D1": "<chosen label> — <user's words verbatim>"}}`. The writer reruns
    with the answer.
  - `arbiter` or `qa` (about committed code): `"rulings": {"T1": {"D1": {"title": "...", "answer": "<chosen label> — <user's words>"}}}`. The
    correction round applies it as a `fixup!` of that slice. Nothing reruns, and the slices on top of it keep building.
