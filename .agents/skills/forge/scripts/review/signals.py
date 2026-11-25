#!/usr/bin/env python3
"""Find review signals in the content of a change, so forge picks critics by what the code does, not only by file names.

Usage:
  signals.py --worktree <wt> --base <ref>            # the branch against its base, plus uncommitted and untracked files
  signals.py --worktree <wt> --commit <sha>          # one commit (a chain slice)

Prints one JSON object: {"lenses": {"<lens>": ["<file>:<line> <snippet>", ...]}, "files": <changed files scanned>}.
Only added or removed lines count; generated files (EF designers, model snapshots, lock files, regenerated contracts) are skipped.
Read-only: it runs git and reads files, nothing else.
"""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

MAX_EVIDENCE = 3

# Each lens: (regex over a changed line, which side counts: "+" added, "-" removed, "+-" both, optional path filter).
RULES = {
    "security": [
        (r"\[Authorize|AllowAnonymous|RequireAuthorization|\.Authorize\w*\(|ClaimsPrincipal|FindFirst\(|IsOwner|OwnerId|Membership|HasAccess|CanAccess|ModuleRole|\bGRANT\b|\bREVOKE\b|Password|Secret|ApiKey|Cookie|Csrf|Cors|RateLimit|Hash\w*\(", "+-", None),
    ],
    "data": [
        (r"migrationBuilder\.(?!Sql\b)\w+|\b(CREATE|ALTER|DROP)\s+(TABLE|INDEX|CONSTRAINT|COLUMN)\b|\bUPDATE\s+\w+\s+SET\b|\bDELETE\s+FROM\b|HasIndex|IsUnique|HasForeignKey|OnDelete\(|HasCheckConstraint|ToTable\(", "+-", None),
        (r"migrationBuilder\.Sql", "+", None),
    ],
    "performance": [
        (r"FOR UPDATE|SKIP LOCKED|ExecuteSql\w*|FromSql\w*|\.Include\(|ToListAsync|ToArrayAsync|BeginTransaction|IsolationLevel|SemaphoreSlim|\block\s*\(|Interlocked\.|Task\.WhenAll|Parallel\.|Channel<|GetOrSetAsync|RemoveByTag\w*|\bwhile\s*\(.*await|foreach\s*\(.*\)\s*$", "+", None),
    ],
    "contracts": [
        (r"Map(Get|Post|Put|Patch|Delete)\(|Produces\w*\(|ProblemDetails|IntegrationEvent|JsonPropertyName|\brecord\s+\w+(Request|Response|Event)\b|class\s+\w+Options\b|ValidateOnStart|ErrorCode", "+-", None),
    ],
    "dx": [
        (r"Map(Get|Post|Put|Patch|Delete)\(|MapGroup\(|WithName\(|Produces\w*\(", "+-", None),
    ],
    "observability": [
        (r"\bMeter\b|Create(Counter|Histogram|UpDownCounter|Gauge)|ActivitySource|StartActivity|LoggerMessage|\.Log(Information|Warning|Error|Critical)\(|SetTag\(|AddTag\(", "+-", None),
    ],
    # A removed assertion or a skipped test is the signal that a test may have been weakened to pass.
    "tests": [
        (r"\bAssert\b|\.Is\w+\(|Throws|\[Test\]|\[Arguments", "-", r"^tests/"),
        (r"Skip\s*=|\[Skip|\.Skip\(", "+", r"^tests/"),
    ],
    "architecture": [
        (r"<ProjectReference|<PackageReference|<FrameworkReference|AddScoped|AddSingleton|AddTransient|AddHostedService|\.Contracts\b", "+-", None),
    ],
}

REMOVED = re.compile(r"^\S+(?: \(removed\))? -")
GENERATED = re.compile(r"\.Designer\.cs$|ModelSnapshot\.cs$|packages\.lock\.json$|^contracts/|/schema\.sql$")


def git(wt, *args):
    return subprocess.run(["git", "-C", wt, *args], check=True, capture_output=True, text=True).stdout


def changed_lines(wt, base, commit):
    """Yield (path, line number or 0, side, text) for every added or removed line of the change."""
    diff = git(wt, "show", "--format=", "-U0", commit) if commit else git(wt, "diff", "-U0", base)
    path, line = None, 0
    for raw in diff.splitlines():
        # A deleted file has "+++ /dev/null": keep the path from its "--- a/" line.
        if raw.startswith("--- ") or raw.startswith("+++ "):
            if raw[4:6] in ("a/", "b/"):
                path = raw[6:]
            continue
        if raw.startswith("diff --git"):
            path = None
            continue
        m = re.match(r"@@ -\d+(?:,\d+)? \+(\d+)", raw)
        if m:
            line = int(m.group(1))
            continue
        if path and raw[:1] in "+-":
            yield path, line if raw[0] == "+" else 0, raw[0], raw[1:]
            if raw[0] == "+":
                line += 1
    if not commit:
        # Untracked new files are part of an uncommitted change: every line is added.
        for path in git(wt, "ls-files", "--others", "--exclude-standard").splitlines():
            try:
                text = (Path(wt) / path).read_text(errors="ignore")
            except OSError:
                continue
            for i, content in enumerate(text.splitlines(), 1):
                yield path, i, "+", content


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--worktree", required=True)
    ap.add_argument("--base", default="origin/main")
    ap.add_argument("--commit")
    a = ap.parse_args()
    compiled = {lens: [(re.compile(rx), sides, re.compile(pf) if pf else None) for rx, sides, pf in rules] for lens, rules in RULES.items()}
    found, files = {}, set()
    for path, line, side, text in changed_lines(a.worktree, a.base, a.commit):
        if GENERATED.search(path):
            continue
        files.add(path)
        for lens, rules in compiled.items():
            # A removed assertion always gets recorded, even after added skips filled the tests lens: it is what turns a
            # refactor into a crud in the Workflow.
            if len(found.get(lens, [])) >= MAX_EVIDENCE and not (lens == "tests" and side == "-" and not any(REMOVED.match(e) for e in found[lens])):
                continue
            for rx, sides, pf in rules:
                if side in sides and (pf is None or pf.search(path)) and rx.search(text):
                    where = f"{path}:{line}" if line else f"{path} (removed)"
                    found.setdefault(lens, []).append(f"{where} {side}{text.strip()[:100]}")
                    break
    json.dump({"lenses": found, "files": len(files)}, sys.stdout)
    print()


if __name__ == "__main__":
    main()
