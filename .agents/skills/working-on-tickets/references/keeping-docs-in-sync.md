# Keeping docs and skills in sync

Documentation and skills drift behind the code when they are only updated in
batches after the fact. Update them as part of the ticket, not afterwards.

## What changes where

| Kind of change | Goes to |
| --- | --- |
| A choice made while implementing (why X over Y) | Decision Log (Plane page) |
| A structural change (module boundaries, events, persistence shape) | Architecture (Plane page) |
| A convention (naming, error handling, testing pattern) | Engineering Standards (Plane page) **and** the matching skill reference |
| Phase or module progress | Roadmap (Plane page) |

## During the ticket (writer, same PR)

Update the skill reference(s) that encode any convention the ticket
introduces or changes, in the same PR as the code. The PR template's "Docs
and skills updated" item tracks this.

## After merge (orchestrator)

1. Record new decisions in the Decision Log.
2. Sync Architecture, Engineering Standards and Roadmap for what changed.
3. Fact-check every statement against `main` before publishing — never
   publish a page from memory or from the PR description alone; re-read the
   merged code first.
4. Use `plane.sh pages`, `plane.sh page-get <name>` and
   `plane.sh page-put <name> --html-file F` (dry-run first for anything
   non-trivial) — Plane is only written through `plane.sh`.

## Rule

Drafts are verified against code, never published from memory. If a
statement can't be checked against `main` right now, leave it out rather than
guess.
