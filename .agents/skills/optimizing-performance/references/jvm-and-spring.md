# JVM and Spring

Guidance, not measured facts. Validate each item with a baseline.

## Threads

- Virtual threads: `spring.threads.virtual.enabled=true` suits blocking I/O (JDBC, HTTP). Watch for pinning in `synchronized` blocks around I/O and for pool limits becoming the real bottleneck (see `database.md`).
- Do not add thread pools for request work; bound concurrency with the connection pool or semaphores.

## Startup and memory

- Production runs with a Java 25 Leyden AOT cache; regenerate it when dependencies change. Measure startup with and without it.
- Prefer fewer beans and lazy optional integrations over blanket `spring.main.lazy-initialization`.
- Size heap from container limits (`-XX:MaxRAMPercentage`), not fixed `-Xmx`.

## Profiling and observability

- JFR for CPU, allocation and lock profiling: `jcmd <pid> JFR.start duration=60s filename=rec.jfr`, analyze in JDK Mission Control.
- Micrometer metrics and OpenTelemetry traces (OTLP) for request latency, DB time, outbox lag. Add a timer or span before optimizing a path you cannot see.
- Modulith observability shows time spent per module.

## Persistence access

- Avoid N+1: fetch joins or batch fetching for aggregate loads; assert query counts in a test when a path is hot.
- Queries (CQRS read side) use projections (records via JPQL constructor expressions or native SQL), never load aggregates to build a list.
- Always paginate collection reads; prefer keyset (cursor) pagination for large tables.
- Keep transactions short: no remote calls inside a command transaction.

## HTTP

- Compress large JSON responses; set cache headers on immutable resources (menus by version, media).
- Avoid serializing lazy JPA associations; responses are records built from projections.
