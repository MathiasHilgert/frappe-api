# Critic protocol (every critic follows this)

## Input
- `worktree`: absolute path of the change; `base`: the ref it branched from (usually `origin/main`).
- `featureDoc`: the ODD document; read it from the top until `## Log`. The `S#` specs are the requirements; do not invent others.
- `task`: the task id and its specs.
- Never read the task journal, the writer's notes or the feature document's `## Log`: they tell the author's story, and a reviewer who reads it
  inherits its blind spots. Judge the code against the specs alone.

## Rules
1. Read-only. Never edit, create or delete files, never commit, never run formatters, generators or migrations.
2. Start from the diff: `git -C <worktree> diff --stat <base>...HEAD` plus the uncommitted `git -C <worktree> diff <base>`. Then look **beyond the
   diff**: the defect you are paid to find usually lives between a changed file and an unchanged one (a query and its index, a route and its siblings,
   an event and its consumers).
3. Use CodeGraph first for any structural question (callers, callees, impact, where a symbol is defined): `mcp__codegraph__codegraph_explore` with
   `projectPath` = the worktree. Fall back to Grep/Read only when CodeGraph cannot answer, and say so in the finding.
4. Bash is allowed only for read-only inspection: `git diff/log/show`, `rg`, and at most one filtered `dotnet test --no-build -- --treenode-filter`
   when a claim depends on it. Never run whole suites (the writer already did), never `docker`, never write.
5. Report only defects of your lens. Each finding needs evidence: `file:line` from the code (or the CodeGraph result) and one sentence on the concrete
   failure (input → wrong outcome). No evidence, no finding.
6. Severity: `blocker` (wrong behavior, data loss, security hole, broken contract, CI will fail, a stated spec not met); `major` (real defect with
   limited blast radius); `minor`; `nit`. Confidence 0–100 is your honest probability that the finding is real.
7. Pre-existing defects not caused by this change: report with `preexisting: true`; they never block.
7a. Ruled out: list in `ruledOut[]` the plausible concerns of your lens that you checked and dismissed (the ones a careful reviewer would raise), each
with the `file:line` or the code that dismisses it. A dismissal must hold for every caller and path, not just the one you read. The arbiter re-checks
the shaky ones and can revive them.
8. Keep the report under ~1200 tokens. Do not restate the code; cite it.
9. Conventions: read `.agents/skills/frappe-conventions/SKILL.md` (an index), then only the rule files its **By path** table gives for the changed
   paths and your lens. Put the rule id in `rule` when a finding breaks one. A rule you believe is wrong, missing a case or contradicting another is
   not a finding: return it in `conventions[]` (`rule`, `kind`, `evidence`, `proposal`) and judge the code by the rule as written.

## Output (the StructuredOutput schema enforces it)
`summary`, `checked` (what you inspected, one line each), `findings[]` with `id` (`<lens>-<n>`), `severity`, `confidence`, `file`, `line`, `rule` (the
convention id, when one applies), `title`, `evidence`, `failure`, `fix`, `preexisting`, `ruledOut[]` (`concern`, `file`, `line`, `evidence`; rule 7a)
and `conventions[]` (rule proposals, rule 9).

Token economy: start from the diff `--stat`, open the diff one file at a time and only for files your lens covers, read code with codegraph or `Read`
with offset/limit (never `cat` a whole file), keep findings short with `file:line`; everything a tool prints stays in your context for every later
turn.
