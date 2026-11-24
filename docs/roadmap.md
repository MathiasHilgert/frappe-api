# Roadmap

Modules are built in the order below. Each one is planned as a whole (one design, one feature document) before its first task.

Notifications comes first because Identity needs to send mail at registration (the activation code): a module that must send a code
cannot ship before the module that delivers it. Notifications reacts to no module at first: it is triggered by tests and by its own
requests, and each later module adds a producer that maps its integration events to notifications.

## Module order

| # | Module | Delivers |
|---|--------|----------|
| 1 | Notifications | Delivery of mail first, and later other channels, for explicit requests and for facts of the modules below: tracked deliveries, suppression and preferences. |
| 2 | Identity | Owners and their sessions: registration, login, logout, credentials and the session lookup every other module relies on. |
| 3 | Access | Who may do what: roles, grants and the authorization check (`AuthorizeAsync`) that protected routes call. |
| 4 | Businesses | Businesses owned by an owner, with their lifecycle, limits and the events other modules react to. |
| 5 | Geo | Reference data: countries, subdivisions, currencies and holiday calendars, read through a query port. |
| 6 | Media | Stored files attached to businesses, served only to callers allowed to see them. |

## Deferred

- **Media authorization through Access.** Every Media read and write goes through Access `AuthorizeAsync`. An architecture test fails the
  build when a Media endpoint or handler reaches storage without it.
- **Fuzzing of hand-written parsers.** Every parser written in this repository (not provided by a library) gets a fuzz target that checks
  it never throws on arbitrary input and that accepted values round-trip.
- **Mutation testing of Domain.** Run a mutation tool over each module's Domain project and treat surviving mutants as missing tests.
