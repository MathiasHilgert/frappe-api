#!/usr/bin/env python3
"""Per-phase and per-agent timings of one forge Workflow run, read from its transcript.

A Workflow script cannot read the clock (Date.now() breaks resume), so timings come from the run's transcript
directory: `journal.jsonl` names each agent (label, phase) and `agent-<id>.jsonl` holds its timestamped turns.

Usage:
  run_timings.py <transcript dir>      # the folder holding journal.jsonl (the Workflow result names it)
  run_timings.py --latest <project transcripts dir>   # newest run under ~/.claude/projects/<project>/

Prints one JSON object:
  wall_min        first agent start to last agent end
  phases          per phase: agents, busy minutes (sum), span minutes (first start to last end), turns, output tokens
  agents          per agent: label, phase, type, model, minutes, orientation minutes (start to first file change, thinking included),
                  turns, tool calls, output tokens, cache write/read tokens
  slowest         the five longest agents
Turns count distinct assistant messages; a cached (resumed) agent has no transcript and is skipped.
Read-only.
"""
import argparse
import json
import re
import sys
from datetime import datetime
from pathlib import Path


def ts(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def agent_stats(path):
    first = last = None
    messages, tools, out_tokens, cache_write, cache_read = set(), 0, 0, 0, 0
    first_edit = None
    for line in path.open(encoding="utf-8", errors="ignore"):
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        stamp = entry.get("timestamp")
        if stamp:
            t = ts(stamp)
            first = t if first is None or t < first else first
            last = t if last is None or t > last else last
        if entry.get("type") != "assistant":
            continue
        message = entry.get("message") or {}
        mid = message.get("id") or entry.get("uuid")
        if mid in messages:
            continue  # one message streamed over several lines: count its usage once
        messages.add(mid)
        usage = message.get("usage") or {}
        out_tokens += usage.get("output_tokens", 0) or 0
        cache_write += usage.get("cache_creation_input_tokens", 0) or 0
        cache_read += usage.get("cache_read_input_tokens", 0) or 0
        for c in message.get("content") or []:
            if not (isinstance(c, dict) and c.get("type") == "tool_use"):
                continue
            tools += 1
            # Orientation ends at the first change: an Edit/Write, or a Bash call that writes a file or scaffolds.
            command = (c.get("input") or {}).get("command", "")
            if first_edit is None and stamp and (c.get("name") in ("Edit", "Write") or re.search(r"(?<![>&0-9])>(?![>&])|\bsed -i|just scaffold|dotnet new", command)):
                first_edit = ts(stamp)
    return first, last, len(messages), tools, out_tokens, cache_write, cache_read, first_edit


def latest_run(root):
    runs = sorted(Path(root).glob("*/subagents/workflows/wf_*/journal.jsonl"), key=lambda p: p.stat().st_mtime)
    if not runs:
        sys.exit(f"no workflow runs under {root}")
    return runs[-1].parent


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run", nargs="?")
    ap.add_argument("--latest")
    a = ap.parse_args()
    run = latest_run(a.latest) if a.latest else Path(a.run)
    labels = {}
    for line in (run / "journal.jsonl").open(encoding="utf-8"):
        entry = json.loads(line)
        if entry.get("type") == "started" and entry.get("agentId"):
            labels[entry["agentId"]] = (entry.get("label") or "", entry.get("phase") or "")

    agents = []
    for path in sorted(run.glob("agent-*.jsonl")):
        aid = path.stem[len("agent-"):]
        first, last, turns, tools, out_tokens, cache_write, cache_read, first_edit = agent_stats(path)
        if first is None:
            continue
        meta_path = path.with_suffix("").with_suffix(".meta.json")
        meta = json.loads(meta_path.read_text()) if meta_path.exists() else {}
        label, phase = labels.get(aid, (meta.get("description", aid), meta.get("workflowPhase", "")))
        agents.append({
            "label": label, "phase": phase or meta.get("workflowPhase", ""), "type": meta.get("agentType"),
            "model": meta.get("model") or meta.get("pickedEffortModel"), "start": first, "end": last,
            "minutes": round((last - first).total_seconds() / 60, 1), "turns": turns, "tools": tools,
            "orientation_min": round((first_edit - first).total_seconds() / 60, 1) if first_edit else None,
            "output_tokens": out_tokens, "cache_write": cache_write, "cache_read": cache_read,
        })
    if not agents:
        sys.exit(f"no agent transcripts in {run}")

    phases = {}
    for ag in agents:
        p = phases.setdefault(ag["phase"] or "?", {"agents": 0, "busy_min": 0.0, "start": ag["start"], "end": ag["end"], "turns": 0, "output_tokens": 0})
        p["agents"] += 1
        p["busy_min"] += ag["minutes"]
        p["start"], p["end"] = min(p["start"], ag["start"]), max(p["end"], ag["end"])
        p["turns"] += ag["turns"]
        p["output_tokens"] += ag["output_tokens"]
    for p in phases.values():
        p["span_min"] = round((p.pop("end") - p.pop("start")).total_seconds() / 60, 1)
        p["busy_min"] = round(p["busy_min"], 1)

    start, end = min(a["start"] for a in agents), max(a["end"] for a in agents)
    for ag in agents:
        ag["start_offset_min"] = round((ag.pop("start") - start).total_seconds() / 60, 1)
        ag.pop("end")
    print(json.dumps({
        "run": str(run),
        "wall_min": round((end - start).total_seconds() / 60, 1),
        "phases": phases,
        "agents": agents,
        "slowest": [f"{a['label']} {a['minutes']} min, {a['turns']} turns" for a in sorted(agents, key=lambda x: -x["minutes"])[:5]],
    }))


if __name__ == "__main__":
    main()
