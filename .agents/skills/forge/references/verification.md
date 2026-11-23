# Fast verification

Time is the constraint. Typical timings on this repo: incremental build after one file 1.5–2 s (solution or one test project), no-op solution build
4.6 s; `dotnet format style` 14.8 s for the solution vs 3.3 s for one project; Platform suite 8 s; Identity and Geo integration suites 25–27 s each;
one integration test class 10 s, of which ~8 s is starting its Postgres container. Compiling is not the bottleneck, and neither are the suites: model
generation dominates a writer run. Whole-file rewrites and one-command turns cost the most; see the work-economy rules in `agents/forge-writer.md`,
`scripts/codemods/run.sh` and `references/pitfalls.md`.

## Inner loop (while writing, RED → GREEN → refactor)
- Navigate and read with CodeGraph and the C# language server (csharp-ls is installed and answers `LSP` hover, definitions, references). Treat LSP
  diagnostics as hints: they lag after edits and can show stale errors; the compiler is the truth.
- Build only the test project that holds the test you are working on: `dotnet build <test project> --no-restore` (it builds what it references,
  incrementally).
- Run only the class or test under change: `dotnet test --project <test project> --no-build -- --treenode-filter "/*/*/<Class>/*"` (or
  `/*/*/<Class>/<Test>`).
- Never run a whole suite inside the loop; never rerun a suite that already passed for code you did not touch since.

## Final gate (once, when the task is done)
Writers run it as one command: `scripts/gate/gate.sh` (one Bash call, `timeout: 600000`), then `gate.sh --rerun` after a fix; inner-loop tests go
through `scripts/gate/test-class.sh <Class>`. The steps below are what the script does; they stay here as the reference.
1. `dotnet build Frappe.slnx --no-restore` once.
2. Then everything independent **in parallel** (`cmd & … ; wait`, one log per command, report each exit code):
   - `dotnet format Frappe.slnx --verify-no-changes --include <changed files>` (one command: whitespace, style and analyzers such as import order;
     `whitespace` alone misses an IMPORTS failure);
   - `dotnet ef migrations has-pending-model-changes` per changed module, and `just db schema` when a migration changed (the committed `**/schema.sql`
     is checked by CI);
   - typos on the changed files (`uvx --from typos==1.52.0 typos --config eng/typos.toml <files>`);
   - squawk on new migrations (`references`: forge/lessons/migrations);
   - `dotnet test --project <p> --no-build` for every test project that covers a changed project (`tests/Frappe.Architecture.Tests` always; Platform
     when `src/Platform` changed). Test projects run side by side safely: each starts its own containers on random ports.
3. On a failure: fix, rerun only the failing project (or class), then rerun only what the fix could affect. Do not rerun the whole gate.

## Who runs what
- Writers: inner loop while writing, the final gate once before returning (and once after a correction round).
- Critics and the arbiter: never run suites; read code, and at most one filtered `--no-build` test when a claim depends on it.
- QA: the slot and Bruno only; it does not rerun the unit or integration suites.

## Possible next speedups (repo changes, offer as tasks)
- Testcontainers reuse in local runs (`WithReuse(true)` plus a label, enabled only by an environment variable, never in CI) would save most of the ~8
  s per class in the inner loop.
- Measure again when the solution grows; revisit `/graph` builds or static-graph restore only past ~30 s no-op builds.

## Models and effort per role
Same mechanical task, three models in parallel: Sonnet 5.5 109 s and 5 tool calls; Opus 5.5 200 s; Haiku 5.5 232 s, 42 tool calls and a build break.
Haiku's raw speed is lost to extra turns, so no role uses it.
| Role | Model | Effort |
|---|---|---|
| Writer | sonnet | per profile: `low` for mechanical/docs/tests, `medium` otherwise |
| Arbiter | opus | medium |
| Critics correctness, security, data (also the fix review after a correction or a QA fix) | opus | medium |
| Critics architecture, contracts, dx, performance | sonnet | medium |
| Critics craft, tests, observability | sonnet | low |
| QA | sonnet | medium |
| Design critics (plan stage) | opus | high |

## Writer hooks (enforced economy)
`agents/forge-writer.md` declares a PreToolUse hook (`scripts/hooks/writer_guard.py`): Write on an existing file is denied (use Edit), and `dotnet
build|test|format` and `just` commands run through `run_trimmed.sh` (exit code kept; failures and summary printed; full log path given;
`FORGE_NO_TRIM=1` opts out). `maxTurns: 250` is only a runaway guard: a capped writer is not resumed. Commands that already pipe their output are not
wrapped. Agent definitions load when a session starts: after changing the frontmatter, restart before trusting it, and check it once (a Write on an
existing file must be denied). The general-purpose fallback in the Workflow carries no hooks.
Prompt cache TTL stays at 5 minutes: a writer's gaps between calls are tool runs that rarely pass 5 minutes (suites take 8-27 s), so a 1 h TTL would
double cache-write cost for nothing.

## Token economy
- Every forge agent sets `omitClaudeMd: true`: the global CLAUDE.md (~24k tokens of orchestrator rules) was loaded by every agent and re-read on every
  turn, ~25-30% of all tokens. What a role needs lives in its definition and the task prompt.
- Critics, arbiter and QA set `maxTurns`; the arbiter runs at `medium`; `deep` caps critics at 6, `standard` at 5. The correctness, security and data
  critics run on Opus: a judge favors code written by its own model, so the lenses that block a change must not share the writers' model. Judge the
  cost with `criticStats` from real runs (`references/retro.md`) before moving any lens back.
- Tool output stays in context for every later turn: critics read the diff `--stat` first and one file at a time, code through codegraph or `Read`
  with offset/limit, never `cat` of whole files; builds and tests use quiet flags and print only failures.
