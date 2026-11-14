<!--
Title format (Conventional Commits, scope is mandatory):
  <type>(<scope>): <imperative summary, lower case, no trailing period>
  types: feat | fix | refactor | perf | test | docs | style | build | ci | chore | revert
  scope: module or area, e.g. geo, catalog, building-blocks, api, worker, infra, solution
  breaking changes: add "!" before ":" and a BREAKING CHANGE note in Technical decisions
  branch: <type>/<kebab-topic> with the title's type, e.g. feat/geo-holidays
  no AI attribution in the title, the description or any commit (CI rejects it)
Examples:
  feat(geo): serve national holidays per country and year
  build(solution): scaffold modular monolith foundation
Sections that do not apply stay, with "None in this PR" and the reason.
-->

## TL;DR

<!--
Tell it as a story, for a senior engineer who reads only this block.
Plain paragraphs only: no ### headings and no &nbsp; spacers inside the TL;DR. GitHub already spaces paragraphs.
Open with the real-world situation, then one bold sentence with what this PR changes about it.
Then short paragraphs, each starting with a bold label.
-->

_Real-world situation that makes this change necessary._

**_One sentence: what this PR changes about that._**

**The problem.** _What hurt, or what would hurt without this._

**What we built.** _One line, then at most three bullets, each starting with a bold phrase._

**How.** _The key mechanism._

**Why this way.** _The tradeoff that justifies the approach, and the alternative it beats._

---

## Summary

<!-- One paragraph: where this change sits in the system and what it unlocks next. -->

## Architecture

<!--
Mermaid diagrams for whatever this change touches: module boundaries and dependency direction (flowchart),
runtime flows (sequenceDiagram), data model (erDiagram), state machines (stateDiagram-v2), delivery plan (timeline/gantt).
Diagram what exists after merge; mark anything planned explicitly as planned.
-->

## Infrastructure

<!-- Containers, schemas, roles, streams, buckets, config keys, env vars, migrations. What changed and how it runs locally. -->

## Events

| Direction | Subject / type | Producer → Consumer | Payload | Guarantees |
|-----------|----------------|---------------------|---------|------------|
| Emitted | | | | |
| Consumed | | | | |

## Observability

<!-- Traces (spans and links), metrics (name, unit, tags), logs, dashboards. Infrastructure telemetry is automatic; call out new business metrics. -->

| Signal | Name | Type / unit | Why it exists |
|--------|------|-------------|---------------|

## Endpoints

<!-- Method, route, auth, rate limit, cache behavior. One example request and response per endpoint, including error cases (Problem Details). -->

```bash
curl -i http://localhost:8080/v1/...
```

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

## Technical decisions

| Decision | Alternatives considered | Why |
|----------|------------------------|-----|

## Pending decisions

| Topic | Options | Blocking? |
|-------|---------|-----------|

## How to verify

<!-- Exact commands a reviewer runs, and what they should observe. -->

## Risk

<!-- What could break, blast radius, rollback. -->
