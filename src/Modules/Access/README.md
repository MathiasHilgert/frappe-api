# Access

Access decides whether a caller may run a use case, and it never owns data, routes or events.

## Doors

| Host | Start here | Loads |
|------|------------|-------|
| Api | [AccessModule](Frappe.Access/AccessModule.cs) `.Api` | `AccessCoreServices`, then `AccessHttp` (error texts, no routes) |
| Worker | [AccessModule](Frappe.Access/AccessModule.cs) `.Worker` | `AccessCoreServices`, `AccessWorkerServices` (the system caller) |
| Migrator | none | Access has no database |

## Features

| Feature | Domain | Use cases | Endpoints |
|---------|--------|-----------|-----------|
| Authorization | none | [AccessGate](Frappe.Access.Application/AccessGate.cs), [Authorizer](Frappe.Access.Application/Authorizer.cs), [UseCaseTable](Frappe.Access.Application/UseCaseTable.cs), [UseCaseCheck](Frappe.Access.Application/UseCaseCheck.cs) | none |

## Metrics (meter `Frappe.Access`)

| Name | Kind | Unit | Tags | Class |
|------|------|------|------|-------|
| `frappe.access.decisions` | Counter | `{decision}` | `request`, `frappe.outcome` | [AccessMetrics](Frappe.Access.Application/Telemetry/AccessMetrics.cs) |

## Events

Publishes: none. Consumes: none.

## Extensions

None.
