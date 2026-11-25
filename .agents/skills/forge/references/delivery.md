# Delivery

QA runs while CI runs: launch the Workflow with `deferQa: true`. It returns `done` with `qaPending` on the tasks or chain heads that need QA. Then
open the PRs below (CI starts), and immediately launch the Workflow again with `mode: "qa"` and `waves: [[<each qaPending entry>]]` (new run, not a
resume). Merge only when both CI and QA pass. A QA defect lands as `fixup!` commits: fold them, push with `--force-with-lease` (CI reruns once) and
continue. Without runtime changes nothing is pending and delivery is as below.

## One PR per slice, stacked

Every slice is delivered as its own PR, stacked on the one before it: never one branch and one PR for the whole feature. A reviewer reads one slice
at a time, within its shape's size (`references/plan.md`), and each slice can be reverted alone. The Workflow still builds a chain on one branch;
delivery cuts it into one branch per slice.

For a chain the Workflow returned `done`:

1. Fold the fixups with `GIT_SEQUENCE_EDITOR="python3 -I <repo>/.agents/skills/forge/scripts/delivery/fold-todo.py" git -C <wt> rebase -i
   origin/main`. Correction writers work on the head, so a fixup for slice k often touches files that slices after k changed; moving it under k
   conflicts. The script moves a fixup under its slice only when no commit it would jump over touched one of its files, and keeps the others in
   place as `fix(<scope>): follow up on <slice>` commits (they land in the PR whose range holds them; its Summary says so). It also amends every
   commit once so the local commit-msg hook strips AI trailers. Check the final tree equals the head's (`git diff <old head> HEAD` is empty).
2. Make every slice's CI meaningful. Run `just pr-check` on the head, then on **every slice commit** (`pr-check` checks one commit type per range:
   run it per type range when a chain mixes `refactor` and `feat` slices). A stacked slice is often not green alone: it uses a seam the next slice
   adds, or its tests only pass once the next slice lands. For each slice that fails alone, pick one and write it in its Summary:
   - **join it** with the next slice into one PR (the default when the pair still fits the size budget);
   - **scope it explicitly**: the Summary names the checks expected to fail, why, and the slice whose PR makes them pass, and the PR says "do not
     merge alone". A red check nobody explained is never acceptable, and a scoped PR merges only together with the slice that turns it green.
   CI runs on every PR whatever its base (`.github/workflows/ci.yml` triggers on `pull_request` with no branch filter), and its change detection
   and changed-line coverage diff against the PR's base (`github.base_ref`), so a stacked PR is checked as its own slice on top of its parent.
3. Cut one branch per slice at its last commit (`git branch <type>/<topic>-<nn>-<slug> <sha>`), push them atomically, open the PRs (PR 1 targets
   main, PR k targets PR k-1's branch; title = the slice's `feat(...)` header, never the first commit of the range), then link them as a native
   GitHub stack: `gh stack link <pr1> ... <prN>` (extension `github/gh-stack`). Each Summary lists the chain.
   The branch type must equal the title type (a `fix(...)` slice goes on `fix/...`; the conventions check fails otherwise). Never rename a
   branch that has an open PR: GitHub closes the PR. If a stack needs a PR replaced, `gh stack unstack <n>` and link the full list again.
4. Merge the whole stack at once with `gh stack merge <top pr> --yes` once every PR is green (or scoped, step 2) and QA passed: GitHub merges the
   stack atomically (all or nothing), so there is no retargeting or rebasing between merges. PRs added later go on top with
   `gh stack link <stack#> <pr>`.
5. **Never delete a base branch while a PR still targets it.** Deleting PR k's branch on merge closes PR k+1, which targets it. When a stack
   merges one PR at a time instead of with `gh stack merge`:
   - first retarget the next PR to main (`gh pr edit <next> --base main`), then merge, then delete the merged branch;
   - never merge a base PR with `--delete-branch`, so not with `scripts/delivery/merge-queue.sh`, which passes it: merge base PRs by hand
     (`gh pr merge <pr> --squash`), and use the queue only for PRs that nothing targets;
   - check the repository does not delete head branches on merge (`gh api repos/{owner}/{repo} --jq .delete_branch_on_merge` is `false`); if it
     is `true`, the retarget must come before every merge.
   After a squash merge, rebase the next branch onto main (`git rebase --onto origin/main <old base sha> <next branch>`), push with
   `--force-with-lease`, and let its CI run again before merging it.

## A single task

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
4. One PR per slice of the planned budget (`references/plan.md`); for a chain, load the `chained-pr` skill and open them stacked in dependency
   order with the chain in each Summary (One PR per slice, stacked, above). Push and open each PR with `.github/pull_request_template.md`; delete
   the template sections that do not apply instead of filling them. Put the critics run, the arbiter verdict, advisories, follow-ups and the QA
   scenarios in Summary / Pending decisions / How to verify.
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
   ask once with the PR list. Merge with `scripts/delivery/merge-queue.sh <pr>...` (never a PR another PR targets: it deletes the branch; see
   One PR per slice, stacked, step 5): PRs that touch different modules merge back to back and main's CI is watched once at the end; it watches
   main between two PRs only when they share an area, and stops when a PR is behind or conflicting (rebase it, `just pr-check`, push, rerun the
   queue from it).
6a. Release PRs. release-please opens its PR as the Actions bot, and a PR opened by that bot does not start the CI workflows. The orchestrator adds
   an assignee and a type label (the metadata check asks for them), then closes and reopens the PR as a user, which triggers CI, and merges when
   it is green. When the user asks for a joint release, the open PRs merge only once all of them are green, and the release PR comes last.
   Pushing the image to GHCR needs the repository listed with Write in the package's Manage Actions access when a package of that name already
   exists; add it there before the first publish, or the push is denied.
7. Clean up: `git worktree remove`, delete merged local branches, release any slot still held (`scripts/slots/slot gc`), remove scratch paths agents
   reported.
8. Update the ODD document: task status, commit, PR, and a Log entry; close the feature with its open follow-ups.
