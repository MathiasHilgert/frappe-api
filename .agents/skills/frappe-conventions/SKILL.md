---
name: frappe-conventions
description: "Trigger: write, review or plan code in frappe-api; a rule id like DATA-04. Index of the repo's code rules: load only the rule files your task or diff needs."
license: Apache-2.0
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract
Use before writing, reviewing or designing anything in this repo. This file is only an index: read the rows that match your task or your diff, then
open only those files under `rules/`. Never load every rule file.

## Hard Rules
1. Load on demand: match your task in **By task** and your changed paths in **By path**; read those rule files only. `design.md` is mandatory for any
   task that creates or changes a type.
2. Cite rules by id (`DATA-04`) in plans, findings and commits. A finding without a rule id is an opinion; say so.
3. A rule is in force unless its card says `Status: alignment pending`; then new code follows it and old code is fixed by the alignment feature (do
   not mix that fix into unrelated work).
4. Exceptions live only in the code, next to the exception: `// Exception to DATA-04: <reason>` or an analyzer suppression whose `Justification`
   starts with the id. Never silently break a rule.
5. Right to object (never dogma): if a rule is wrong, missing a case, or contradicts another, keep following the current rule and return a
   **convention proposal** (below). Stop only when the rule makes the task impossible. No agent edits a rule file on its own.
6. Tools first: when a card names a tool, trust the tool's verdict over your reading; when a rule could be checked by a tool and is not, say so in
   your proposal.
7. Process (when to grill, plan, review, deliver) belongs to `/forge`; this skill only says what good code looks like here.

## Convention proposal (writers and critics return it; the arbiter filters; the user decides)
```json
{ "rule": "DATA-04", "kind": "change | exception | inconsistency | missing",
  "evidence": "path:line and why the rule fails here",
  "proposal": "the new rule text, the exception wording, or the fix" }
```
Outcomes: keep the rule · exception (marked in code, rule 4) · change the rule (the orchestrator edits the card).

## By task
| If you will… | Read |
|---|---|
| create or change any type | design, craft, csharp |
| start a feature, decide where it lives | criteria, architecture, structure |
| add a use case (command or query) | cqrs, domain, uow, structure |
| model an aggregate, value object, error | domain, design |
| read another module's data or react to it | architecture, events, criteria |
| publish or consume an integration event | events, contracts |
| add or change a table, column, index, constraint | data, migrations, performance |
| write a migration | migrations, data |
| add or change an HTTP route, request, response, error code | http, contracts, security |
| cache something, use Valkey | caching, performance |
| add a setting, a job, an outbound client | configuration, performance |
| add metrics, logs, alerts, dashboards | observability |
| touch auth, sessions, secrets, PII, grants | security |
| write or change tests | tests |
| add a NuGet package or platform library | criteria |

## By path (reviewers: union of every matching row)
| Diff touches | Read |
|---|---|
| `src/Modules/*/*.Domain/**` | domain, design, csharp |
| `src/Modules/*/*.Application/**` | cqrs, domain, uow, design |
| `src/Modules/*/*.Infrastructure/**` | architecture, structure, data, caching, uow |
| `**/Migrations/**`, `*Configuration.cs`, `*DbContext.cs` | migrations, data, performance |
| `src/Modules/*/*.Http/**`, `contracts/openapi/**` | http, contracts, security |
| `src/Modules/*/*.Contracts/**`, `contracts/events/**` | events, contracts, architecture |
| `**/*Metrics*.cs`, `deploy/observability/**` | observability |
| `**/*Options*.cs`, `**/*Registration.cs`, `src/Hosts/**` | configuration, architecture |
| `src/Platform/**` | architecture, criteria, design |
| `tests/**` | tests |
| `Directory.*.props`, `.editorconfig`, `eng/**` | craft, criteria |
| any `*.cs` | craft, csharp |

## Card format (every rule file)
A file opens with a one-line-per-rule summary, then one card per rule:
```markdown
### DATA-05 · Every key, index and check carries our lower-case prefix
- **Rule:** one or two imperative sentences.
- **Why:** the consequence it prevents.
- **Enforced by:** tool or test name | review.
- **Good:** short snippet. **Bad:** short snippet.
- **Status:** in force | alignment pending.
```

## Rule files
architecture · structure · design · domain · cqrs · uow · data · migrations · caching · performance · http · contracts · events · observability ·
security · configuration · tests · craft · criteria · csharp — all under `rules/<name>.md`.
