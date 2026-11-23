# Known pitfalls (read before you start; each one already cost a writer several attempts)

- StyleCop SA1201 reports a location-less "A <unknown> should not follow a field" for a C# 14 `extension(...)` block placed after fields or consts: put extension blocks first in the class.
- `dotnet format style --severity info` without `--diagnostics` also applies IDE0005 and deletes usings that global usings need; always pass explicit `--diagnostics`.
- rtk condenses build output: a build that looks green may have failed; read the error count line, and rerun the tests after any format pass.
- `dotnet ef migrations add` in a fresh worktree fails with NETSDK1004 until `dotnet restore` of the module Infrastructure project; a migration name starting with a digit yields a class named `_<timestamp>_...`: start names with a letter.
- Squawk accepts `SET NOT NULL` only after a validated `CHECK (col IS NOT NULL)` in the concatenated migration SQL.
- `docker compose run` has no `--no-build`; use `--pull never` (it reuses a loaded image).
- Bruno: `bru` is not installed; run `npx -y @usebruno/cli@4.3.0` directly. Slots need `FRAPPE_OBS_PORT_OFFSET=10000` while the shared observability stack runs.
- TUnit: `Assert.Throws<T>(Action)` returns the exception synchronously, so a throw can be the Act and be asserted afterwards.
- Central package management: test packages are pinned in `Directory.Packages.props` (`Microsoft.Extensions.TimeProvider.Testing` included); never add `VersionOverride`.
- Shared test helpers live in `tests/Shared/Frappe.Testing` (`WaitUntil.TrueAsync`, 20 ms polling, 30 s timeout).
- The skills folder and the feature journal exist only in the main checkout: read them by absolute path from the worktree.
- A PR title and every commit need a lower-case scope even for `ci` (`ci(migrations): …`); commitlint fails `ci: …`.
- `dotnet format whitespace` alone misses analyzer/import-order failures: run `dotnet format Frappe.slnx --verify-no-changes --include <files>` (one command).
- After any migration, `just db schema` and commit the `**/schema.sql` it writes; CI diffs both a fresh and an upgraded database against it. A branch older than a merged migration of another module fails that check until rebased.
- `dotnet ef migrations add` names the class `_<timestamp>_<Name>`: rename it in the migration and its Designer. After a rebase over another migration of the same module, the Designer's BuildTargetModel must equal the snapshot.
- zsh does not word-split `$files`: run multi-file commands through `bash -c` or arrays; quote `--include='*.cs'` globs.
- EF wraps a Postgres 40P01 deadlock in DbUpdateConcurrencyException: register an OnPostgres rule before OnConcurrency to map it.
