# FAPI-3 — platform: Set up agent skills for working on Frappé

Plane: [FAPI-3](https://app.plane.so/nulled-software/browse/FAPI-3/) (module platform, size L). Branch: `chore/fapi-3-agent-skills`.

## Objective
Repository-versioned agent skills that define how we work, write code, test and optimize, with small context via index skills routing to focused files.

## Decisions
- `.agents/` is the source of truth; `.claude` is a symlink to `.agents`. `AGENTS.md` root index; `CLAUDE.md` symlink to it.
- Own skills (gerund names): `working-on-tickets`, `writing-code`, `testing-code`, `optimizing-performance`. Claude Code only discovers top-level skill dirs, so "sub-skills" are reference files one level deep from each SKILL.md.
- Third-party: jabrena/plinth (321 unit, 322 integration, 305 modulith), supabase/agent-skills supabase-postgres-best-practices.
- Workflow: parallelism check, complexity → model routing, Plane states Todo → In Progress → In Review → Done, git worktrees under `~/Projects/nulled/frappe-api-worktrees/<TICKET>`, shared docker compose + Testcontainers reuse + one database per ticket, PR template, wait CI, report to human, feedback → one subagent fixes.
- Deterministic Plane helper script; key from `PLANE_API_KEY` env.
- Authoring rules: Anthropic best practices + Gentle style guide (third-person descriptions with triggers, SKILL.md body 180–500 tokens, references one level deep, TOC for files >100 lines, checklists, 3 eval scenarios per skill).

## TDD
Strict TDD enabled; no production code. Helper script verified by running it against Plane (read-only commands) and shellcheck.

## Tasks
- [x] T1 Structure: `.agents/`, `.claude` symlink, `AGENTS.md` + `CLAUDE.md` symlink
- [x] T2 Install third-party skills into `.agents/skills`
- [x] T3 Plane helper script
- [x] T4 `working-on-tickets` skill + references + agent definitions
- [x] T5 `writing-code` skill + references
- [x] T6 `testing-code` skill + references (Testcontainers reuse, per-ticket DB)
- [x] T7 `optimizing-performance` skill + references
- [x] T8 Eval scenarios, README link, PR opened with green CI

## Progress / evidence
- T1 `28e210c`: `.agents/{skills,agents}`, `.claude -> .agents`, `CLAUDE.md -> AGENTS.md` (git mode 120000), not ignored (`git check-ignore` rc=1).
- T2 `cd5acea`: copied from clones (plinth `9a3dc29`, supabase/agent-skills `8331f91`), `diff -r` shows only the added upstream LICENSE files; recorded in `.agents/skills/THIRD_PARTY.md`.
- T3 `fb892c4`: shellcheck 0.11.0 clean; `show FAPI-1`, `list`, `list in-progress` OK against Plane; no-op `move FAPI-3 in-progress` OK; error paths (unknown key, bad state, missing key, unreadable file) exit 1 with messages. `comment`/`create` not exercised live (would mutate Plane).
- T4 `6c11718`: skill + 6 references + 4 subagents + evals. Cleanup text in `running-in-parallel.md` adjusted in `e1b5a79`.
- T5 `15dda5c`, T6 `e1b5a79`, T7 `9608490`: skills + references + evals (evals shipped with each skill instead of in T8).
- T8 `0df8700`: README section, `./gradlew check` green locally. PR #8 opened, 7/7 checks green, FAPI-3 moved to In Review.

## Next step
Human review of PR #8; on "merge": squash merge and move FAPI-3 to Done.
