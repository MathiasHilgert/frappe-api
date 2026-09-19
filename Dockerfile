# syntax=docker/dockerfile:1
#
# Multi-stage build for the Frappé API.
#
# 1. build   — compiles the layered Spring Boot jar with the Gradle wrapper on a full JDK image.
# 2. layers  — splits the jar into Spring Boot's layers (dependencies change far less often than our own code).
# 3. train   — a training run that only refreshes the Spring context (-Dspring.context.exit=onRefresh, the
#              documented way to train a Java 25 AOT cache without serving traffic, running a scheduled task or
#              publishing an event), then bakes a Java 25 AOT cache (JEP 483) from the classes that refresh used.
#              Needs a live, build-owned Postgres database and NATS on localhost only — see README, "Build and run
#              the image locally"; `docker build` must run with `--network=host`.
# 4. final   — the minimal JRE 25 runtime: layered jar, AOT cache, fixed non-root UID/GID.
#
# No secrets: the training stage never uses ARG or ENV for its local-only, build-owned defaults (BuildKit's
# SecretsUsedInArgOrEnv check has nothing to flag); they are exported inline in the one RUN that needs them and
# never appear in any layer or build arg (verify with `docker history --no-trunc` and `docker inspect .Config.Env`).
#
# Base images are pinned by digest; Dependabot's docker ecosystem keeps them current (.github/dependabot.yml).

FROM eclipse-temurin:25-jdk-noble@sha256:2feab631bffce6236d8bb5261a4abe19a8d6f85bad1c01166f74686c983d011f AS build
WORKDIR /workspace
COPY gradlew ./
COPY gradle ./gradle
COPY build.gradle.kts settings.gradle.kts gradle.properties package.json package-lock.json ./
# Warm the dependency and Node caches before the source changes, so editing src/ does not invalidate this layer.
# `dependencies` (not `help`) actually resolves every configuration's classpath, including the ones bootJar needs.
RUN --mount=type=cache,target=/root/.gradle ./gradlew --no-daemon dependencies
COPY src ./src
RUN --mount=type=cache,target=/root/.gradle ./gradlew --no-daemon bootJar

# The AOT cache is tied to the exact HotSpot build that creates it (JEP 483); layers, train and final all use the
# same eclipse-temurin:25-jre-alpine image (musl) so the cache built in "train" loads in "final". Trade-off: musl's
# libc is not glibc, so the JDK/JRE community build matters more than usual — kept because the final runtime never
# needs anything glibc-only (the JDK build alone, no node/npm), and alpine's smaller base keeps the image lean.
FROM eclipse-temurin:25-jre-alpine@sha256:2ca9adf44f5c29d28ecd26cf92d75cc0c66b7f32bfd839a4439e363a8b428af8 AS layers
WORKDIR /workspace
COPY --from=build /workspace/build/libs/*.jar app.jar
RUN java -Djarmode=tools -jar app.jar extract --layers --launcher --destination extracted

FROM eclipse-temurin:25-jre-alpine@sha256:2ca9adf44f5c29d28ecd26cf92d75cc0c66b7f32bfd839a4439e363a8b428af8 AS train
WORKDIR /app
COPY --from=layers /workspace/extracted/dependencies/ ./
COPY --from=layers /workspace/extracted/spring-boot-loader/ ./
COPY --from=layers /workspace/extracted/snapshot-dependencies/ ./
COPY --from=layers /workspace/extracted/application/ ./
# SerialGC: the deploy target (README, "Build and run the image locally") is a small single/two-vCPU VPS running one
# instance, where SerialGC's single-threaded, no-background-thread collector has the lowest memory and CPU footprint;
# G1's regions and concurrent marking threads only pay off with more cores and a bigger heap than this target has.
# The AOT cache can archive heap state, so train and final must pin the identical GC (and RAM/OOM flags) or a
# mismatch invalidates the archive at startup.
ENV JVM_RUNTIME_FLAGS="-XX:+UseSerialGC -XX:MaxRAMPercentage=75 -XX:+ExitOnOutOfMemoryError"
# Host and port only (no credential): compose's own host port, so a machine that already has something on the
# standard Postgres/NATS ports (README, "Local infrastructure") can still build. Never a secret, so plain ARGs are
# fine; validated below to stay on localhost/127.0.0.1 regardless of what is passed.
ARG FRAPPE_TRAIN_DB_HOST=localhost
ARG FRAPPE_TRAIN_DB_PORT=5432
ARG FRAPPE_TRAIN_NATS_HOST=localhost
ARG FRAPPE_TRAIN_NATS_PORT=4222
# Record + create phase, foreground, no background process and no `|| true`: a non-zero exit (bad training DB, a
# context that never refreshes, Flyway failing on real migrations) fails the build. `-Dspring.context.exit=onRefresh`
# is Spring Boot's documented way to train an AOT/CDS cache: the context refreshes (so every bean that would exist at
# startup exists here too) and the process exits immediately after, before serving a request, running a scheduled
# task or publishing an event. Every credential below is a local-only, build-owned default, never an ARG or ENV.
#
# BuildKit's docker-container driver (used in CI, docker/build-push-action) injects OTEL_EXPORTER_OTLP_*_ENDPOINT
# (unix:///dev/otel-grpc.sock, its own tracing socket, since executor/oci/spec_linux.go's getTracingSocket()) into
# every RUN step's environment for its own diagnostics; the classic builder does not, which is why this only broke
# in CI. Our OTel starter picks that up and fails to start (unix:// is not a valid OTLP HTTP/gRPC endpoint), so
# every OTEL_* variable is stripped before running Java, and export is also disabled explicitly through Boot
# properties: belt and braces, and the training run must never export telemetry anywhere regardless of environment.
RUN --network=host set -eu; \
    for v in $(env | grep -o '^OTEL_[A-Z0-9_]*' || true); do unset "$v"; done; \
    case "$FRAPPE_TRAIN_DB_HOST" in localhost|127.0.0.1) ;; \
      *) echo 'Training must run against a localhost/127.0.0.1 Postgres, never a remote one.' >&2; exit 1 ;; \
    esac; \
    case "$FRAPPE_TRAIN_NATS_HOST" in localhost|127.0.0.1) ;; \
      *) echo 'Training must run against localhost/127.0.0.1 NATS, never a remote one.' >&2; exit 1 ;; \
    esac; \
    export SPRING_PROFILES_ACTIVE=local; \
    export FRAPPE_DB_URL="jdbc:postgresql://${FRAPPE_TRAIN_DB_HOST}:${FRAPPE_TRAIN_DB_PORT}/frappe_image_train"; \
    export FRAPPE_APP_PASSWORD='frappe_app'; \
    export FRAPPE_OWNER_PASSWORD='frappe_owner'; \
    export FRAPPE_VALKEY_URL='redis://localhost:6379'; \
    export FRAPPE_SECRET_PEPPER='local-development-pepper-not-a-secret'; \
    case "$FRAPPE_DB_URL" in \
      jdbc:postgresql://localhost:*/frappe_image_train|jdbc:postgresql://127.0.0.1:*/frappe_image_train) ;; \
      *) echo 'Training must target database frappe_image_train, the one the image build owns (see README,' \
              '"Build and run the image locally"); refusing to touch anything else.' >&2; \
         exit 1 ;; \
    esac; \
    java $JVM_RUNTIME_FLAGS -XX:AOTMode=record -XX:AOTConfiguration=app.aotconf -Dspring.context.exit=onRefresh \
      -Dfrappe.nats.url="nats://${FRAPPE_TRAIN_NATS_HOST}:${FRAPPE_TRAIN_NATS_PORT}" \
      -Dmanagement.opentelemetry.enabled=false \
      -Dmanagement.tracing.export.otlp.enabled=false \
      -Dmanagement.otlp.metrics.export.enabled=false \
      -Dmanagement.otlp.logging.export.enabled=false \
      -Dmanagement.logging.export.otlp.enabled=false \
      org.springframework.boot.loader.launch.JarLauncher; \
    test -s app.aotconf
RUN java $JVM_RUNTIME_FLAGS -XX:AOTMode=create -XX:AOTConfiguration=app.aotconf -XX:AOTCache=app.aot \
      org.springframework.boot.loader.launch.JarLauncher --version
RUN rm -f app.aotconf

FROM eclipse-temurin:25-jre-alpine@sha256:2ca9adf44f5c29d28ecd26cf92d75cc0c66b7f32bfd839a4439e363a8b428af8 AS final
# Container-aware JVM (reads cgroup limits by default since JDK 10), the AOT cache, a memory ceiling that leaves
# headroom under the container limit, a hard crash instead of a thrashing OOM, and one virtual thread per request —
# GC must match "train" exactly (see its comment) or the AOT cache's archived heap state is rejected at startup.
ENV JAVA_TOOL_OPTIONS="-XX:+UseSerialGC -XX:MaxRAMPercentage=75 -XX:+ExitOnOutOfMemoryError -XX:AOTCache=/app/app.aot" \
    SPRING_THREADS_VIRTUAL_ENABLED=true
# Fixed UID/GID (not the next free one at build time) so a bind-mounted volume or a policy that pins by numeric id
# stays correct across rebuilds.
RUN addgroup -S -g 10001 frappe && adduser -S -u 10001 -G frappe frappe
WORKDIR /app
COPY --from=layers --chown=10001:10001 /workspace/extracted/dependencies/ ./
COPY --from=layers --chown=10001:10001 /workspace/extracted/spring-boot-loader/ ./
COPY --from=layers --chown=10001:10001 /workspace/extracted/snapshot-dependencies/ ./
COPY --from=layers --chown=10001:10001 /workspace/extracted/application/ ./
COPY --from=train --chown=10001:10001 /app/app.aot ./app.aot
USER 10001:10001
EXPOSE 8080
# Liveness only: readiness depends on the database and Valkey being reachable, which is Kamal's job to check before
# routing traffic (deploy ticket), not this container's own healthcheck. See README for the readiness endpoint.
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
  CMD wget -q -O /dev/null http://localhost:8080/actuator/health/liveness || exit 1
ENTRYPOINT ["java", "org.springframework.boot.loader.launch.JarLauncher"]
