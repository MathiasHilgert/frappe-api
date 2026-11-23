#!/usr/bin/env python3
"""Follow-up fix rate: the outcome metric of forge's review.

A change "needed a follow-up" when, within --window days after it reached the branch, a commit fixed it:
- `rate` (precise): a later commit names it in a `Fixes: <sha>` trailer (forge writes one on every fix of a merged
  change, `references/delivery.md`), the convention the Linux kernel uses for the same metric;
- `overlap_rate` (upper bound): a later `fix`/`revert` commit touched any of its source files. Shared files make it
  overcount; use it only when the history has no trailers yet.
A review that works keeps both low; cost metrics (tokens, minutes) say nothing about it.

Usage:
  followup_fixes.py --repo <path> [--branch origin/main] [--since <YYYY-MM-DD>] [--window 60] [--paths src/] [--list]

Only changes old enough to have had their full window are counted (the newest ones are still "open").
Prints one JSON object. Read-only: it runs git log, nothing else.
"""
import argparse
import json
import re
import subprocess
from datetime import datetime, timedelta, timezone

FIX = re.compile(r"^(fix(\(.+\))?!?:|revert(\(.+\))?!?:|Revert )", re.IGNORECASE)
# Generated and shared project files: touching one says nothing about fixing the same behavior.
GENERATED = re.compile(r"\.Designer\.cs$|ModelSnapshot\.cs$|packages\.lock\.json$|/schema\.sql$|\.csproj$|Directory\.(Build|Packages)\.props$|Program\.cs$|/Migrations/|/Resources/")


def commits(repo, branch, since, prefixes):
    """First-parent commits, oldest first: (sha, date, subject, files under the prefixes)."""
    args = ["git", "-C", repo, "log", "--first-parent", "--reverse", "--format=\x1e%H\x1f%cI\x1f%s\x1f%(trailers:key=Fixes,valueonly,separator=%x20)", "--name-only", branch]
    if since:
        args.insert(5, f"--since={since}")
    out = subprocess.run(args, check=True, capture_output=True, text=True).stdout
    for block in out.split("\x1e")[1:]:
        head, _, names = block.partition("\n")
        sha, date, subject, fixes = head.split("\x1f", 3)
        files = {f for f in names.split("\n") if f and f.startswith(prefixes) and not GENERATED.search(f)}
        yield sha, datetime.fromisoformat(date), subject, files, re.findall(r"\b[0-9a-f]{7,40}\b", fixes)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True)
    ap.add_argument("--branch", default="origin/main")
    ap.add_argument("--since")
    ap.add_argument("--window", type=int, default=60)
    ap.add_argument("--paths", nargs="*", default=["src/"])
    ap.add_argument("--list", action="store_true", help="include each followed-up change and the fix that followed it")
    a = ap.parse_args()

    log = list(commits(a.repo, a.branch, a.since, tuple(a.paths)))
    now = datetime.now(timezone.utc)
    window = timedelta(days=a.window)
    counted, followed, overlapped, still_open = 0, [], 0, 0
    for i, (sha, date, subject, files, _) in enumerate(log):
        if FIX.match(subject) or not files:
            continue
        if date + window > now:
            still_open += 1
            continue
        counted += 1
        precise = overlap = None
        for later_sha, later_date, later_subject, later_files, later_fixes in log[i + 1:]:
            if later_date - date > window:
                break
            if precise is None and any(sha.startswith(f) for f in later_fixes):
                precise = (later_sha, later_subject, later_date)
            if overlap is None and FIX.match(later_subject) and files & later_files:
                overlap = (later_sha, later_subject, later_date)
        if overlap:
            overlapped += 1
        if precise:
            followed.append({"change": f"{sha[:9]} {subject}", "fix": f"{precise[0][:9]} {precise[1]}", "days": (precise[2] - date).days})
    result = {
        "window_days": a.window,
        "changes": counted,
        "followed_up": len(followed),
        "rate": round(len(followed) / counted, 4) if counted else None,
        "overlap_followed_up": overlapped,
        "overlap_rate": round(overlapped / counted, 4) if counted else None,
        "still_open": still_open,
    }
    if a.list:
        result["followups"] = followed
    print(json.dumps(result))


if __name__ == "__main__":
    main()
