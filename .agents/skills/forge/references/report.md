# Final report

The last thing `/forge` produces. A person with no context sits down, reads it once and understands what was built and why.

## Format

Always in the chat (user's choice, to avoid switching context). The terminal does not draw mermaid, so diagrams are compact ASCII or markdown tables
that read well as text. Only when the user asks for a page (to share it), build it as an artifact: load the `artifact-design` skill first, write it to
the scratchpad, publish it and give the link.

## Rules

- Storytelling first, minimal, idiot-proof: short sentences, everyday words, every technical term glossed the first time.
- Explain what and why; tests and test counts only in one line, if at all.
- Facts come from the ODD document, the Workflow results, the PRs and the code at the merged commit (CodeGraph); never from memory.
- No file paths in the body except in "Where to look"; no internal names (critic, arbiter, `T1`, `S3`).

## Sections (drop any the change does not touch)

1. **In one sentence.** What changed for users or operators.
2. **The story.** Before → the problem → what we did → after. Three to six sentences.
3. **What we built.** One line per capability.
4. **How it fits.** A small ASCII diagram of the pieces involved and how they talk (Api, Worker, Postgres schema of each module, Valkey,
   NATS/JetStream, Mailpit, external providers), only the parts this change touches plus their direct neighbors, the new parts marked. Then one line
   per layer touched (domain, application, infrastructure, Http) saying what lives there now.
5. **The data.** How the database looks after the change: a table per table added or changed (columns, keys, constraints, indexes worth knowing),
   marking what is new; growth and retention in one line.
6. **Events.** Emitted (name, when, who listens) and subscribed (name, from whom, what we do with it); an ASCII sequence when a flow crosses modules.
7. **Contracts.** Routes, options and settings added or changed, and whether anything breaks for clients.
8. **Signals.** Metrics, logs and alerts an operator now has.
9. **Decisions that shaped it.** The user's choices that changed the result, each with the option it beat, one line each.
10. **Delivered as.** The PRs in order (number, one-line purpose, merged or open), and the chain when there are several.
11. **Left for later.** Follow-ups, accepted risks.
12. **Where to look.** ODD document, main files, the Engram topics of the feature.
