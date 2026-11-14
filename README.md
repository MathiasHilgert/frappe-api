# Frappé API

[![Tests](https://github.com/MathiasHilgert/frappe-api/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/MathiasHilgert/frappe-api/actions/workflows/tests.yml)
[![Code quality](https://github.com/MathiasHilgert/frappe-api/actions/workflows/code-quality.yml/badge.svg?branch=main)](https://github.com/MathiasHilgert/frappe-api/actions/workflows/code-quality.yml)
[![License: proprietary](https://img.shields.io/badge/license-proprietary-lightgrey)](LICENSE)

The backend of Frappé, the operating system for restaurants and cafés in Latin America.

## About

Frappé is a modular monolith on .NET 11: one codebase and one deployable image, split into business modules
that own their data and talk to each other through integration events. Each module is a hexagon (domain, application, infrastructure, HTTP adapter) with CQRS,
and the cross-cutting work (persistence, messaging, caching, scheduling, localization, observability, health)
lives once in a shared platform layer.

**Status: early.** The platform layer is being built: results, use cases, localization, aggregates, health probes and the HTTP conventions (Problem Details, language
negotiation, caching) are in place.
The modules arrive after it.

## Tech stack

| Area | Choice |
|------|--------|
| Runtime | .NET 11, ASP.NET Core, OpenAPI with a [Scalar](https://scalar.com/) reference |
| Data | PostgreSQL, EF Core (Npgsql), NodaTime |
| Messaging | NATS JetStream, CloudEvents |
| Caching | FusionCache over Valkey |
| Scheduling | Quartz.NET |
| Observability | OpenTelemetry into Grafana's LGTM stack (Loki, Grafana, Tempo, Mimir) |
| Testing | TUnit, Testcontainers, WireMock, NetArchTest |
| Tooling | Docker Compose, [just](https://github.com/casey/just), Renovate, release-please |

## Getting started

### Prerequisites

- The .NET 11 SDK pinned in [`global.json`](global.json)
- [just](https://github.com/casey/just#installation)

### Build

```bash
just build             # dotnet build Frappe.slnx, warnings are errors
```

`just` alone lists every command.

## Testing

```bash
just test all        # every suite
```

`tests/` mirrors `src/`. The platform suite (`tests/Platform/Frappe.Platform.Tests`) runs in process.

## Conventions

| Topic | Rule |
|-------|------|
| Commits and PR titles | [Conventional Commits](https://www.conventionalcommits.org/) with a mandatory scope: `feat(platform): add a health probe` |
| Branches | `<type>/<kebab-topic>`, with the type of the title |
| Merging | Squash only: the PR title becomes the commit on `main` |
| Code quality | Warnings are errors; every method stays within cognitive complexity 15 and cyclomatic complexity 10 |
| CI | Every required check is named after what it guarantees, such as "Code compiles without warnings" |

## License

Copyright © 2026 Nülled Software. All rights reserved. This repository is publicly visible, but it is **not open
source**: the source code is proprietary, and no right to use, copy, modify or distribute it is granted without
written permission. See [LICENSE](LICENSE).
