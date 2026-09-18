# Frappé API

Frappé is a multi-tenant SaaS platform for restaurant chains operating
multiple branches. This repository hosts the backend API: a modular
monolith built with Spring Boot and Spring Modulith, using domain events
to keep modules loosely coupled while sharing a single deployable
application.

Documentation (product requirements, architecture decisions, tickets)
lives in Plane, not in this repository.

## License

Proprietary. Copyright (c) 2026 Frappé. All rights reserved. See
[LICENSE](LICENSE). This repository is public for transparency only; no
usage rights are granted.

## Prerequisites

- Java 25 (Temurin recommended, e.g. via [mise](https://mise.jdx.dev)) — the
  application compiles and runs on this version via the Gradle toolchain.
- Java 21 (Temurin) to *run Gradle itself*: `palantir-java-format`, used by
  Spotless, does not yet support JDK 25 as the host JVM. Point `JAVA_HOME`
  at a JDK 21 install when invoking `./gradlew`; Gradle's toolchain support
  auto-provisions JDK 25 for compiling and running the app and tests.
- Docker (for local infra and Testcontainers-based tests)
- [gitleaks](https://github.com/gitleaks/gitleaks) (`mise use -g gitleaks` or via Homebrew)

## Local infrastructure

Start Postgres and NATS (JetStream enabled):

```bash
docker compose up -d
```

Spring Boot's Docker Compose support will also start these services
automatically when running the application locally.

## Build and test

Run the full quality gate (formatting check, module boundary
verification, tests):

```bash
./gradlew check
```

## Git hooks

This repository ships a versioned pre-commit hook that runs
[gitleaks](https://github.com/gitleaks/gitleaks) against staged changes
to catch accidentally committed secrets.

Enable it once per clone:

```bash
git config core.hooksPath .githooks
```
