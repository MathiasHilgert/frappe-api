# Delivery

QA runs while CI runs: launch the Workflow with `deferQa: true`. It returns `done` with `qaPending` on the tasks or chain heads that need QA. Then
open the PRs below (CI starts), and immediately launch the Workflow again with `mode: "qa"` and `waves: [[<each qaPending entry>]]` (new run, not a
resume). Merge only when both CI and QA pass. A QA defect lands as `fixup!` commits: fold them, push with `--force-with-lease` (CI reruns once) and
continue. Without runtime changes nothing is pending and delivery is as below.

For a chain the Workflow returned `done`:

1. Fold the fixups: `git -C <wt> -c sequence.editor=: rebase -i --autosquash origin/main`. On a conflict, abort and keep that fixup as its own
   `fix(<scope>): ...` commit on the slice's PR.
2. Run `just pr-check` once on the head.
3. Cut one branch per slice at its commit (`git branch <type>/<topic>-<n> <sha>`) and open stacked PRs (`chained-pr` skill): PR 1 targets main, PR k
   targets PR k-1's branch; each Summary lists the chain.
4. Merge in order with `scripts/delivery/merge-queue.sh` (squash; the commit carries the PR body). After PR k merges, GitHub retargets PR k+1 to main:
   `git rebase --onto origin/main <old PR k head> <PR k+1 branch>`, push with `--force-with-lease`, continue.

For each task the Workflow returned `done` (verdict pass or blockers resolved, QA pass or not required):

1. Read the diff (`git -C <wt> diff --stat origin/main`) and rerun one reported check yourself before committing.
2. Commit in the worktree: Conventional Commit with a scope (`<type>(<scope>): <summary>`, header ≤ 100 chars, branch type = title type); no AI
   attribution (CI rejects it). A fix of behavior that an earlier merged change introduced carries one trailer per culprit: `Fixes: <12-char sha on
   main> ("<its subject>")` (find it with `git log -S`/`git blame`); it feeds the follow-up fix rate (`references/retro.md`). Put the same line in the
   PR body so the squash keeps it.
3. Run `just pr-check` in the worktree before every push (commit/title conventions, full format, build, pending model changes, contracts and schema
   dumps up to date, unique Bruno seq). It mirrors the CI checks that fail after a push (scope, IMPORTS, stale dump, compile after rebase), each
   costing a full CI round.
3a. Rebase on `origin/main` when other PRs merged meanwhile; regenerate any EF migration whose snapshot conflicts (`dotnet ef migrations add`, rename
the generated `_<timestamp>_` class, restore hand-written `Up`/`Down`), rerun the touched suites.
4. One PR per slice of the planned budget (`references/plan.md`); for a chain, load the `chained-pr` skill and open them in dependency order with the
   chain in each Summary. Push and open each PR with `.github/pull_request_template.md`; delete the template sections that do not apply instead of
   filling them. Put the critics run, the arbiter verdict, advisories, follow-ups and the QA scenarios in Summary / Pending decisions / How to verify.
4a. The body follows the template fully: the TL;DR story (situation, bold one-sentence change, The problem, What we built, How, Why this way), then
   every section that applies, and the ones that do not deleted. Architecture holds real Mermaid diagrams of what exists after merge (module
   boundaries and dependency direction as a flowchart, runtime flows as a sequenceDiagram, data as an erDiagram), written from the diff and not
   from the plan; planned pieces are marked as planned. Before `gh pr create` and `gh pr edit`, save the body to a file and run
   `node .agents/skills/forge/scripts/delivery/check-mermaid.mjs <body file>` (mermaid 11.4.1 and jsdom 24.1.3 are installed into a temp
   directory on first use): it parses every mermaid block and exits 1 on an invalid one or on a body with none. Several PRs due at once: one
   agent per PR may write its body, each from its own diff, and the orchestrator runs the check on each file.
5. Watch CI in the background (`gh pr checks <n> --watch`) and never idle on it: while a PR's CI runs, prepare the next PR of the chain (its slice,
   branch and checks, based on the previous branch) or other pending work. On a failure read the failed log, fix in the worktree, push, and rebase the
   chain above it.
6. Merge (squash, with the PR body as the commit body: never pass `--body ""`) only when CI is green and the user has authorized merging; otherwise
   ask once with the PR list. Merge with `scripts/delivery/merge-queue.sh <pr>...`: PRs that touch different modules merge back to back and main's CI
   is watched once at the end; it watches main between two PRs only when they share an area, and stops when a PR is behind or conflicting (rebase it,
   `just pr-check`, push, rerun the queue from it).
6a. Release PRs. release-please opens its PR as the Actions bot, and a PR opened by that bot does not start the CI workflows. The orchestrator adds
   an assignee and a type label (the metadata check asks for them), then closes and reopens the PR as a user, which triggers CI, and merges when
   it is green. When the user asks for a joint release, the open PRs merge only once all of them are green, and the release PR comes last.
   Pushing the image to GHCR needs the repository listed with Write in the package's Manage Actions access when a package of that name already
   exists; add it there before the first publish, or the push is denied.
7. Clean up: `git worktree remove`, delete merged local branches, release any slot still held (`scripts/slots/slot gc`), remove scratch paths agents
   reported.
8. Update the ODD document: task status, commit, PR, and a Log entry; close the feature with its open follow-ups.
