# Keeping Plane in sync

All commands: `.agents/skills/working-on-tickets/scripts/plane.sh` (from the repo root).

## State per step

| Step | Command |
| --- | --- |
| Work starts (worktree created) | `plane.sh move FAPI-N in-progress` |
| PR open and checks green | `plane.sh move FAPI-N in-review` |
| PR squash-merged | `plane.sh move FAPI-N done` |
| Dropped by the human | `plane.sh move FAPI-N cancelled` |

Add a comment only for facts a reader of the ticket needs later (PR link, scope change agreed with the human): `plane.sh comment FAPI-N "PR: <url>"`.

## Creating tickets

Create only when the human asks, just-in-time, one module at a time.

1. Title: `<module>: <imperative outcome>` (e.g. `identity: Open and close person sessions`).
2. Body HTML with sections in order: Context (2–4 lines), Scope, Out of scope, Acceptance (Given/When/Then), Invariants (rule + why), Contracts (endpoints, events, public Api), Dependencies (blocked by / blocks), Done when.
3. Only decided things; open questions become a separate `type:spike` ticket.
4. Size for one PR of ~400 changed lines; split otherwise.
5. Mermaid allowed, no colors or styles.
6. Run:

```bash
plane.sh create --title "identity: Open and close person sessions" \
  --module identity --labels module:identity,type:feature,size:M --html-file /path/body.html
```

The script minifies the HTML, sets state Todo and assigns the Plane module. Tables need `colwidth` on every `th`/`td` summing ~1000 or Plane renders them collapsed.
