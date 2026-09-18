# Choosing models

| Ticket | Writer | Reviewers |
| --- | --- | --- |
| S | `ticket-writer` (sonnet) | 1 × `code-reviewer` (sonnet) |
| M | `ticket-writer` (sonnet) | 1 × `code-reviewer-deep` (opus) |
| L, or any sensitive area | `ticket-writer-deep` (opus) | 2 blind: `code-reviewer-deep` (opus) + `code-reviewer` (sonnet) |
| Fix from human PR feedback | `ticket-writer` (sonnet) | none unless the fix changes behavior |
| Fix from feedback in a sensitive area | `ticket-writer-deep` (opus) | 1 × `code-reviewer-deep` (opus) |

Rules:

- Pick the higher row when size and sensitivity disagree.
- Reviewer fixes use the same writer tier as the original ticket.
- Pass `model` explicitly on every subagent call.
