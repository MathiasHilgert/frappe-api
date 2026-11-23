# Frappé API

The backend of Frappé, the operating system for restaurants and cafés in Latin America.

Frappé is a modular monolith on .NET 11: one codebase and one deployable image, split into business modules that own
their data and talk to each other through integration events. Each module is a hexagon (domain, application,
infrastructure, HTTP adapter), and the cross-cutting work lives once in a shared platform layer.

## Run it locally

```bash
cp deploy/.env.example deploy/.env   # local-only placeholder values; choose your own if the machine is shared
just stack up                        # postgres, valkey, nats, Migrator, Api, Worker and Grafana, Loki, Tempo, Mimir
just stack health                    # readiness of the Api and the Worker
```

The Api serves `http://localhost:8080/docs/` and `/openapi/v1.json`; Grafana is on `http://localhost:3000` with its
datasources only (no dashboards). `just` lists every other command, `CONTRIBUTING.md` explains how to work here.

This software is proprietary; see [LICENSE](LICENSE).
