# Frappé API

The backend of Frappé, the operating system for restaurants and cafés in Latin America.

Frappé is a modular monolith on .NET 11: one codebase and one deployable image, split into business modules that own
their data and talk to each other through integration events. Each module is a hexagon (domain, application,
infrastructure, HTTP adapter), and the cross-cutting work lives once in a shared platform layer.

This software is proprietary; see [LICENSE](LICENSE).
