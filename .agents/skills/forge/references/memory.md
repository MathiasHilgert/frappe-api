# Engram at every stage

Engram is mandatory in `/forge`, project `frappe-api` only. Memory is bookkeeping: do it before composing the reply, never instead of it.

## Access (in this order)
1. MCP tools when loaded (`mem_current_project` must resolve to `frappe-api`; then `mem_search`, `mem_get_observation`, `mem_save`, `mem_update`).
2. Otherwise the CLI from the repo root: `engram search "<query>" --project frappe-api --limit 10`; `engram save "<title>" "<content>" --type <type>
   --topic <topic> --project frappe-api`. Saving again with the same `--topic` updates that observation in place (verified), so one topic = one living
   observation.
3. Both fail: stop and `AskUserQuestion` (start the daemon with `engram run -d`, continue this once without memory and save later, or stop). Never
   continue silently.

## What is worth saving (selective)
Decisions with their reason, non-obvious root causes, real constraints, and what someone needs to resume the feature. Never routine progress, raw
dumps, secrets, tokens or personal data. Content shape: **What**, **Why**, **Where** (files, PRs), **Learned**. Quote the user's decisions verbatim.

Before every save: search the topic; if an observation covers it, save to that same topic (update) keeping every still-valid fact and noting what was
superseded; a new subject gets a new, deliberate topic.

## Topics per feature
| Topic | Type | Holds |
|---|---|---|
| `odd/<feature>/tasks` | `architecture` | The full ODD document (mirror) and its path; the file is the source |
| `forge/<feature>/decisions` | `decision` | Every user decision (grill, design, mid-run answers), verbatim, with date and the option it beat |
| `forge/<feature>/design` | `architecture` | The approved design: layers, module communication, schema, contracts, signals, and what the design critics changed |
| `forge/<feature>/delivery` | `decision` | PRs, merges, CI incidents, follow-ups left open |
| `forge/review/suppressions` | `decision` | Rules for findings the user declined (`references/communication.md`), passed as `suppressions` |
| `forge/metrics/critics` | `discovery` | Per-critic totals, passed as `lensStats` (`references/retro.md`) |
| `forge/metrics/outcomes` | `discovery` | Follow-up fix rate per retro, dated |
| `forge/lessons/<area>` | `discovery` / `bugfix` | Retro lessons that apply beyond this feature (`references/retro.md`) |

## When (every stage)
| Stage | Read | Write |
|---|---|---|
| 0 Recall | `forge/lessons`, `odd/<feature>/tasks`, `forge/<feature>/decisions` (resume), and the feature's keywords | — |
| 1 Grill | Prior decisions on the same area (search keywords) | Each answered block to `forge/<feature>/decisions` |
| 2 Design review + plan | — | `forge/<feature>/design`; `odd/<feature>/tasks` once the document exists |
| 3–5 Workflow | — | Mid-run answers to `decisions`; agents' `learnings[]` and taken non-blocking decisions worth keeping; mirror refresh at each task transition |
| 6 Delivery | — | `forge/<feature>/delivery`; final mirror refresh |
| 7 Retro | `forge/lessons/<area>` before saving | Lessons per `references/retro.md` |

## Resume an interrupted feature
Search `odd/<feature>/tasks` and `forge/<feature>/decisions`, read them in full, read the ODD file itself, reconcile (the file wins; refresh the
mirror from it), then continue with the first unfinished task.
