# Known pitfalls (read before you start; each one already cost a writer several attempts)

- StyleCop SA1201 reports a location-less "A <unknown> should not follow a field" for a C# 14 `extension(...)` block placed after fields or consts:
  put extension blocks first in the class.
- `dotnet format style --severity info` without `--diagnostics` also applies IDE0005 and deletes usings that global usings need; always pass explicit
  `--diagnostics`.
- rtk condenses build output: a build that looks green may have failed; read the error count line, and rerun the tests after any format pass.
- `dotnet ef migrations add` in a fresh worktree fails with NETSDK1004 until `dotnet restore` of the module Infrastructure project; a migration name
  starting with a digit yields a class named `_<timestamp>_...`: start names with a letter.
- Squawk accepts `SET NOT NULL` only after a validated `CHECK (col IS NOT NULL)` in the concatenated migration SQL.
- `docker compose run` has no `--no-build`; use `--pull never` (it reuses a loaded image).
- Bruno: `bru` is not installed; run `npx -y @usebruno/cli@4.3.0` directly. Slots need `FRAPPE_OBS_PORT_OFFSET=10000` while the shared observability
  stack runs.
- TUnit: `Assert.Throws<T>(Action)` returns the exception synchronously, so a throw can be the Act and be asserted afterwards.
- Central package management: test packages are pinned in `Directory.Packages.props` (`Microsoft.Extensions.TimeProvider.Testing` included); never add
  `VersionOverride`.
- Shared test helpers live in `tests/Shared/Frappe.Testing` (`WaitUntil.TrueAsync`, 20 ms polling, 30 s timeout).
- The skills folder and the feature journal exist only in the main checkout: read them by absolute path from the worktree.
- A PR title and every commit need a lower-case scope even for `ci` (`ci(migrations): …`); commitlint fails `ci: …`.
- `dotnet format whitespace` alone misses analyzer/import-order failures: run `dotnet format Frappe.slnx --verify-no-changes --include <files>` (one
  command).
- After any migration, `just db schema` and commit the `**/schema.sql` it writes; CI diffs both a fresh and an upgraded database against it. A branch
  older than a merged migration of another module fails that check until rebased.
- `dotnet ef migrations add` names the class `_<timestamp>_<Name>`: rename it in the migration and its Designer. After a rebase over another migration
  of the same module, the Designer's BuildTargetModel must equal the snapshot.
- zsh does not word-split `$files`: run multi-file commands through `bash -c` or arrays; quote `--include='*.cs'` globs.
- EF wraps a Postgres 40P01 deadlock in DbUpdateConcurrencyException: register an OnPostgres rule before OnConcurrency to map it.
- Commit messages carry no trailers (no Co-Authored-By, no Claude-Session, no AI attribution); CI rejects them.
- Run `node .github/scripts/line-length.mjs` (150 characters) before committing; `scripts/gate/gate.sh` runs it as a step.
- A slice copied into another branch loses the coverage that module tests gave it: measure changed-line coverage with `scripts/gate/coverage.sh`
  before committing (CI fails under 80%).
- CI builds `deploy/.env` by appending `ci-<hex>` to every `.env.example` line that ends in `=`: a base64 placeholder with `=` padding breaks.
  Use unpadded base64 (a length that is a multiple of 3 bytes); `pr-check` checks it.
- The secret scan (gitleaks, `generic-api-key`) flags committed high-entropy placeholders: write them as base64 of text starting with
  `local-only-` (allow-listed in `.gitleaks.toml`), never a random-looking string. `pr-check` runs gitleaks when Docker is available.
- Razor HTML-encodes non-ASCII and quotes: assert accented copy on an email's text part, not on its HTML.
- Gmail ignores SVG and `prefers-color-scheme`: email logos go as inline CID PNG images; dark mode is for Apple Mail, iOS and Outlook.
- Tests never wait for real time. Every clock goes through `TimeProvider` and tests inject `FakeTimeProvider`. A floor or a delay finishes by
  advancing the fake clock past it in one step; never pace it in real time (a loop of `Advance(10 ms)` + `Task.Delay(5)` costs about 0.5 s of real
  time per call). A test that needs many attempts (limits, caps) drives the limiter fake or its budget directly, not 100 real calls.
- Test hosts hash passwords with the cheapest Argon2id settings the options allow (`Identity:Passwords:MemoryMebibytes` at its minimum,
  `Iterations` 1). Only the hasher's own tests use production parameters.
- Budget: a test class finishes in under 60 s (`test-class.sh` kills it there) and one test in under 2 s. When the timeout hits, read the log
  before running it again; three reruns of a hanging class cost a correction 6 minutes once.
