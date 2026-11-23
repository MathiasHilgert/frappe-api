# Roadmap

Modules are built in the order below. Each one is planned as a whole (one design, one feature document) before its first task.

## Module order

| # | Module | Delivers |
|---|--------|----------|
| 1 | Identity | Owners and their sessions: registration, login, logout, credentials and the session lookup every other module relies on. |
| 2 | Access | Who may do what: roles, grants and the authorization check (`AuthorizeAsync`) that protected routes call. |
| 3 | Businesses | Businesses owned by an owner, with their lifecycle, limits and the events other modules react to. |
| 4 | Geo | Reference data: countries, subdivisions, currencies and holiday calendars, read through a query port. |
| 5 | Notification | Delivery of mail and other messages triggered by integration events from the modules above. |
| 6 | Media | Stored files attached to businesses, served only to callers allowed to see them. |

## Deferred

- **Media authorization through Access.** Every Media read and write goes through Access `AuthorizeAsync`. An architecture test fails the
  build when a Media endpoint or handler reaches storage without it.
- **Fuzzing of hand-written parsers.** Every parser written in this repository (not provided by a library) gets a fuzz target that checks
  it never throws on arbitrary input and that accepted values round-trip.
- **Mutation testing of Domain.** Run a mutation tool over each module's Domain project and treat surviving mutants as missing tests.
