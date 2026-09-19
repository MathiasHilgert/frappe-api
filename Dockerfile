# syntax=docker/dockerfile:1
#
# Multi-stage build for the Frappé API.
#
# 1. build   — compiles the layered Spring Boot jar with the Gradle wrapper on a full JDK image.
# 2. layers  — splits the jar into Spring Boot's layers (dependencies change far less often than our own code).
# 3. train   — a real (training) run of the app that records which classes are used, then builds a Java 25 AOT
#              cache from that recording. Needs live Postgres, NATS and Valkey (see README, "Build and run the
#              image locally"); `docker build` must run with `--network=host` so the container can reach them on
#              localhost. If AOT training fails (no compose services reachable), the build fails loudly instead of
#              silently shipping an image without a cache.
# 4. final   — the minimal JRE 25 runtime: layered jar, AOT cache, non-root user.
#
# No secrets: every FRAPPE_* value the training stage sets is a local-profile, non-secret default matching
# compose.yaml (verify with `docker history`).

FROM eclipse-temurin:25-jdk-noble AS build
WORKDIR /workspace
COPY gradlew ./
COPY gradle ./gradle
COPY build.gradle.kts settings.gradle.kts gradle.properties package.json package-lock.json ./
# Warm the dependency and Node caches before the source changes, so editing src/ does not invalidate this layer.
RUN --mount=type=cache,target=/root/.gradle ./gradlew --no-daemon help
COPY src ./src
RUN --mount=type=cache,target=/root/.gradle ./gradlew --no-daemon bootJar

# The AOT cache is tied to the exact HotSpot build that creates it (JEP 483); layers, train and final all use the
# same eclipse-temurin:25-jre-alpine image so the cache built in "train" loads in "final".
FROM eclipse-temurin:25-jre-alpine AS layers
WORKDIR /workspace
COPY --from=build /workspace/build/libs/*.jar app.jar
RUN java -Djarmode=tools -jar app.jar extract --layers --launcher --destination extracted

FROM eclipse-temurin:25-jre-alpine AS train
WORKDIR /app
COPY --from=layers /workspace/extracted/dependencies/ ./
COPY --from=layers /workspace/extracted/spring-boot-loader/ ./
COPY --from=layers /workspace/extracted/snapshot-dependencies/ ./
COPY --from=layers /workspace/extracted/application/ ./
# Local-profile, non-secret defaults matching compose.yaml (overridable at build time; nothing here is a real
# credential — see docs/secrets.md). SPRING_PROFILES_ACTIVE=local keeps Scalar/mail on the same path bootRun uses.
ARG FRAPPE_DB_URL=jdbc:postgresql://localhost:5432/frappe
ARG FRAPPE_APP_PASSWORD=frappe_app
ARG FRAPPE_OWNER_PASSWORD=frappe_owner
ARG FRAPPE_NATS_URL=nats://localhost:4222
ARG FRAPPE_VALKEY_URL=redis://localhost:6379
ARG FRAPPE_SECRET_PEPPER=local-development-pepper-not-a-secret
ENV SPRING_PROFILES_ACTIVE=local \
    FRAPPE_DB_URL=${FRAPPE_DB_URL} \
    FRAPPE_APP_PASSWORD=${FRAPPE_APP_PASSWORD} \
    FRAPPE_OWNER_PASSWORD=${FRAPPE_OWNER_PASSWORD} \
    frappe.nats.url=${FRAPPE_NATS_URL} \
    FRAPPE_VALKEY_URL=${FRAPPE_VALKEY_URL} \
    FRAPPE_SECRET_PEPPER=${FRAPPE_SECRET_PEPPER}
# Record phase: run the app for real against the live services above and stop it as soon as it is healthy, so the
# JVM records the classes an ordinary startup and health probe actually use.
RUN --network=host set -eu; \
    java -XX:AOTMode=record -XX:AOTConfiguration=app.aotconf \
      org.springframework.boot.loader.launch.JarLauncher & \
    pid=$!; \
    tries=0; \
    until wget -q -O /dev/null http://localhost:8080/actuator/health || [ "$tries" -ge 60 ]; do \
      tries=$((tries + 1)); sleep 1; \
    done; \
    kill -TERM "$pid"; \
    wait "$pid" || true; \
    test -s app.aotconf
# Create phase: turn the recorded configuration into the AOT cache used at startup.
RUN java -XX:AOTMode=create -XX:AOTConfiguration=app.aotconf -XX:AOTCache=app.aot \
      org.springframework.boot.loader.launch.JarLauncher --version
RUN rm -f app.aotconf

FROM eclipse-temurin:25-jre-alpine AS final
# Container-aware JVM: reads the cgroup memory/CPU limits (default since JDK 10) and starts one virtual thread per
# task via Tomcat's virtual executor (Spring Boot's spring.threads.virtual.enabled).
ENV JAVA_TOOL_OPTIONS="-XX:AOTCache=/app/app.aot" \
    SPRING_THREADS_VIRTUAL_ENABLED=true
RUN addgroup -S frappe && adduser -S frappe -G frappe
WORKDIR /app
COPY --from=layers --chown=frappe:frappe /workspace/extracted/dependencies/ ./
COPY --from=layers --chown=frappe:frappe /workspace/extracted/spring-boot-loader/ ./
COPY --from=layers --chown=frappe:frappe /workspace/extracted/snapshot-dependencies/ ./
COPY --from=layers --chown=frappe:frappe /workspace/extracted/application/ ./
COPY --from=train --chown=frappe:frappe /app/app.aot ./app.aot
USER frappe
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
  CMD wget -q -O /dev/null http://localhost:8080/actuator/health || exit 1
ENTRYPOINT ["java", "org.springframework.boot.loader.launch.JarLauncher"]
