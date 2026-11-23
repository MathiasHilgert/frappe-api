# TL briefing (shown after the plan, before asking for go)

The reader is a tech lead with five features open and no time. They read this once, top to bottom, and must be able to say go or stop.

## Rules
- Story first, then facts. Plain words; every technical term the first time gets a three-word gloss.
- No file paths, no test names, no spec numbers in the body (they are in the ODD document; link it once at the end).
- Every section is optional except "The story" and "What we build": drop a section the change does not touch instead of writing "none".
- Max ~250 words. Bullets of one line. Bold the one thing in each section the reader must not miss.
- Say what is deliberately not done.

## Template (in the user's language)

**<Feature> — briefing**

**The story.** Two or three sentences: the situation today, what goes wrong or is missing, and what changes after this ships.

**What we build.** 2–4 bullets, each one capability a user or an operator gets.

**How.** The approach in 2–3 bullets: which module owns it, which layer each rule lives in, the patterns used (and why that one, in a few words): e.g.
"integration event, so Billing never waits on Identity".

**Data.** Tables or columns added or changed, how they grow, the one migration risk.

**Contracts.** Routes, events and options added or changed, and whether any client breaks. OpenAPI and Bruno are updated with them.

**Signals.** Metrics (technical and business), what an operator alerts on.

**Impact.** What existing behavior changes and for whom; what could break and how it is contained; what is reused instead of written new.

**Risks and what we leave out.** The riskiest part and how it is contained; what is explicitly out of this change.

**Plan.** Tasks and waves in one line each ("2 tasks in parallel, then 1"), the PRs and their order when the budget splits the work, the depth of
review and whether QA runs. ODD document: `odd/tasks/<feature>.md`.
