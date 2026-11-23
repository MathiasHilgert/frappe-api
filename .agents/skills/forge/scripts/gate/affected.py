#!/usr/bin/env python3
"""Test projects a change can break, from the ProjectReference graph.

Usage: affected.py <worktree> <changed path>...   (paths relative to the worktree)
Prints one test project directory per line (relative). A test project is affected when it contains a changed file or
references, directly or transitively, a project that contains one. A change to shared build files (Directory.*.props,
global.json, NuGet.config, *.slnx, eng/) affects every test project. tests/Frappe.Architecture.Tests always runs.
Read-only.
"""
import re
import sys
from pathlib import Path

SHARED = re.compile(r"(^|/)Directory\.(Build|Packages)\.(props|targets)$|^global\.json$|^NuGet\.config$|\.slnx$|^eng/")
ALWAYS = "tests/Frappe.Architecture.Tests"


def main():
    wt = Path(sys.argv[1]).resolve()
    changed = [p for p in sys.argv[2:] if p]
    projects = {}  # project dir (relative) -> set of referenced project dirs (relative)
    for csproj in wt.glob("**/*.csproj"):
        rel = csproj.relative_to(wt)
        if rel.parts[0] in ("bin", "obj") or "bin" in rel.parts or "obj" in rel.parts:
            continue
        refs = set()
        for inc in re.findall(r'<ProjectReference\s+Include="([^"]+)"', csproj.read_text(errors="ignore")):
            target = (csproj.parent / inc.replace("\\", "/")).resolve()
            try:
                refs.add(str(target.parent.relative_to(wt)))
            except ValueError:
                pass
        projects[str(rel.parent)] = refs
    # Runnable test apps only: helper libraries under tests/ (Frappe.Testing, *.Testing) hold no tests of their own.
    tests = sorted(p for p in projects if p.startswith("tests/") and p.endswith("Tests"))
    if any(SHARED.search(p) for p in changed):
        print("\n".join(t for t in tests if (wt / t).is_dir()))
        return

    def owner(path):
        best = None
        for proj in projects:
            if (path == proj or path.startswith(proj + "/")) and (best is None or len(proj) > len(best)):
                best = proj
        return best

    touched = {o for o in (owner(p) for p in changed) if o}
    affected = set()
    for test in tests:
        seen, stack = set(), [test]
        while stack:
            cur = stack.pop()
            if cur in seen:
                continue
            seen.add(cur)
            stack.extend(projects.get(cur, ()))
        if seen & touched:
            affected.add(test)
    if (wt / ALWAYS).is_dir():
        affected.add(ALWAYS)
    print("\n".join(sorted(affected)))


if __name__ == "__main__":
    main()
