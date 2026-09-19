# Shipping a PR

## Commit and push

- Conventional Commits: `<type>(<module>): <imperative summary>`, English, no AI attribution or `Co-Authored-By`.
- Keep tests and docs in the same commit as the behavior they cover.
- `git push -u origin <branch>`.

## Open the PR

Title = Conventional Commit of the squash result, e.g. `feat(identity): open and close person sessions`.

Body follows `.github/pull_request_template.md`: keep applicable sections (TL;DR, Ticket link `https://app.plane.so/nulled-software/browse/FAPI-N/`, Description, User story, Architecture with mermaid without colors, Observability (table of metrics and spans the change exposes), Events (table of events published or consumed with version, publisher, consumers, subject, payload, idempotency key and description; "None: <reason>" when empty), Proposed developer experience, Decisions made, Pending decisions, Verification with RED/GREEN evidence, `./gradlew check` and, when routes change, the manual calls from `trying-endpoints.md`). Delete sections that do not apply.

```bash
gh pr create --base main --title "<title>" --body-file /path/body.md
```

## Wait for CI

```bash
gh pr checks <n> --watch
```

On failure: read the failing log (`gh run view <id> --log-failed`), fix in the worktree, push, watch again. Maximum 3 attempts; then stop and report the failure to the human.

## Hand over

1. `plane.sh move FAPI-N in-review`.
2. Report: PR URL, checks table, reviewer verdict, decisions made, open questions.

## Human says "merge"

```bash
gh pr merge <n> --squash
plane.sh move FAPI-N done
```

Then clean up the worktree and database (`running-in-parallel.md`).

## Human leaves comments

1. Read them: `gh pr view <n> --comments` and `gh api repos/{owner}/{repo}/pulls/<n>/comments`.
2. Spawn one fixer (see `choosing-models.md`) with the comments verbatim and the worktree path.
3. Fixer commits, reruns `./gradlew check`, pushes.
4. Watch CI again, report what changed per comment.
