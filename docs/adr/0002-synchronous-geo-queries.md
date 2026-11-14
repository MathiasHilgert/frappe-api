# ADR 0002: A synchronous query contract for Geo (`IGeoQueries`)

- **Status:** accepted (2026-11-14)
- **Context owner:** Geo (`src/Modules/Geo/Frappe.Geo.Contracts`)

## Context

Modules talk through integration events: a module publishes a fact through its outbox, and the others react
through their inbox. Nobody reaches into another module's tables or calls its services. That keeps modules
deployable apart and keeps one module's outage away from another's writes.

Geo is mostly asked questions, not told facts. Other modules need to know whether a date is a business day in a
jurisdiction under a policy, the next N business days from a date, what kind of day a date is for payroll, whether a
country or subdivision code exists, and what Geo covers for a country. They need the answer while they serve their
own request, such as computing a payment due date or validating an address form. An event cannot answer a
question.

The alternatives were:

| Option | Cost |
|--------|------|
| Each consumer keeps a copy of the calendars, fed by `HolidaysChanged` | Every consumer re-implements the business-day rule (weekends, policies, sector calendars, subdivisions, coverage), which is what Geo owns. The copies drift, and "not covered" becomes "no holidays" by accident. |
| Consumers call the HTTP API | A network hop inside one process, a second error model, and authentication between modules. |
| A synchronous in-process contract | Couples the caller to Geo being available in the same process. |

## Decision

`Frappe.Geo.Contracts` exposes `IGeoQueries`, an explicit exception to "modules talk through integration events":

- It is read-only. Nothing another module calls through it changes Geo's state. Changes still leave Geo only as
  events (`HolidaysChanged`).
- Its types are primitives and records of `Frappe.Geo.Contracts`: `DateOnly`, strings, enums and answer records.
  Nothing from Geo's domain, NodaTime or `Frappe.Results` leaks.
- A gap in Geo's data is an answer, never an exception and never a false negative. `CalendarStatus.NotCovered`
  says Geo lacks a calendar or rules. `CalendarStatus.UnknownJurisdiction` says the code does not exist. A caller
  cannot mistake missing data for "no holidays".
- `Frappe.Geo.Application` implements it over the same use cases the HTTP API serves, so the rule is computed once.
  Business-day answers read the cached effective calendars, and Geo's own consumer of `HolidaysChanged` evicts them.
- A consumer that derives data from the calendar, such as a stored due date, still subscribes to `HolidaysChanged`
  to recompute it. The query answers now; the event says when "now" changed.

## Consequences

- A caller depends on Geo's database and cache being reachable from its process. A Geo outage fails the caller's
  request instead of degrading later. This is acceptable while every module runs in the same hosts.
- The contract versions like a public API. Members and fields may be added. Changing or removing one is a new
  member, and callers migrate before the old one goes.
- We revisit this if a consumer must run where Geo does not, or if a measured call volume needs a local
  projection. The consumer would then build its projection from `HolidaysChanged` plus a bulk read, and this
  contract would remain for the rest.
