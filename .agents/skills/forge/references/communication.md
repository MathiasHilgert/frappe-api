# Communication

The orchestrator is the only one who talks to the user. Agents talk to the orchestrator and to each other.

## Agents → orchestrator: decision requests
An agent that needs a choice it may not make (product, scope, trade-off, an edit outside its surfaces, a spec that contradicts the code) does not
guess and does not ask in prose. It returns a `decisions[]` entry:

| Field | Content |
|---|---|
| `id` | `D<n>`, unique within the task |
| `title` | ≤ 8 words, the choice itself |
| `context` | ≤ 2 plain sentences: what is happening and why it matters |
| `options[]` | 2–4 entries `{label, consequence}` |
| `recommended` | the label of the option the agent recommends, and why in one sentence |
| `blocking` | `true` when the task cannot continue without it |

Non-blocking decisions are taken with the recommended option, written to the journal and reported at the end. The orchestrator never forwards this
raw; it rewrites it as a decision card.

## Orchestrator → user: the decision card
The user runs several features at once and has not seen this one for hours. Every question must make sense to someone who just walked into the room.
One decision per question, in the user's language; up to four independent questions may share one `AskUserQuestion` call, never one that depends on
another's answer.

**Never** in a card: internal names (arbiter, critic, advisory, profile, wave, `T1`, `S3`, `D2`, run ids), file paths, class or test names, acronyms
without a gloss. A technical word that cannot be avoided gets its meaning in parentheses the first time: "migración (el script que cambia la
estructura de la base)".

### `header` (≤ 12 chars)
The feature in everyday words the user would use for it ("Outbox", "Negocios", "Login"), never a slug.

### `question` — three short paragraphs separated by blank lines
1. **Where we are.** "Estamos <building what, for what> — <which step: diseñando / programando / revisando / probando / por entregar>."
2. **What happened.** One or two sentences that tell the situation as a story, with a concrete example or analogy when it helps. Say what is at stake
   for the product, not for the code.
3. **The question.** One line, ending in `?`.

### `options` (2–4)
- `label`: what we would do, as a verb (≤ 5 words): "Arreglarlo ahora", "Dejarlo para después". Recommended first, with " (Recomendado)".
- `description`: one line, the consequence in plain words, including time when it matters ("suma ~10 minutos").
- `preview` on every option (single-select only), rendered as a small card:

```
Si elegís esto
──────────────
Qué hacemos:   <one line>
Qué ganás:     <one line>
Qué arriesgás: <one line, or "nada relevante">
Cuánto lleva:  <time or "nada extra">
```

### After the answer
Record it verbatim in the ODD `## Log` and in Engram (`forge/<feature>/decisions`), and pass it back to the Workflow as `answers[taskId][decisionId]`.

When the user declines a review finding (an advisory, or a blocker they call deliberate), ask in the same card set whether to stop raising that kind
of finding. On yes, write a rule that names the kind, not the instance ("no requests for XML docs on private members", not "line 42"), and append `{
lens, rule, reason }` (reason: the user's words and the date) to Engram `forge/review/suppressions`. Every launch passes that list as `suppressions`;
only the arbiter sees it, and it never suppresses a blocker.

## Agent ↔ agent: the task journal
- Path: `<repo>/odd/tasks/<feature>.journal/<task-id>.md` (git-ignored, outside the worktree; the orchestrator creates the folder).
- Every agent but the critics reads it before starting; the writer, the arbiter and QA append one section on finishing (critics stay read-only): `##
  <agent> · <round>` with decisions taken (and why), assumptions, things the next agent must know, open concerns. Append only; never rewrite another
  agent's section.
- What each stage also receives in its prompt from the Workflow: the arbiter gets the writer's notes; critics get neither the notes nor the journal
  (they review blind, `references/critic-protocol.md`); the correction writer gets the blockers with the critics' evidence; QA gets the writer's notes
  and the arbiter's advisories.
