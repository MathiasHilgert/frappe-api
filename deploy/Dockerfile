# syntax=docker/dockerfile:1
# One image, three entrypoints: /app/api/Frappe.Api (default), /app/worker/Frappe.Worker, /app/migrator/Frappe.Migrator.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:11.0.100-resolute
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:11.0.0-resolute-chiseled-extra

# Project and lock files only, in their folders: the restore layer below changes only when a project or a package changes.
FROM ${SDK_IMAGE} AS projects
WORKDIR /source
COPY src/ src/
RUN find src -type f ! -name '*.csproj' ! -name 'packages.lock.json' -delete && find src -type d -empty -delete

FROM --platform=$BUILDPLATFORM ${SDK_IMAGE} AS build
ARG TARGETARCH
# A pipe fails the step when any command in it fails, not only the last one.
SHELL ["/bin/bash", "-o", "pipefail", "-c"]
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props NuGet.config .editorconfig ./
COPY eng/CodeMetricsConfig.txt eng/
COPY --from=projects /source/src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    for host in Api Worker Migrator; do \
      dotnet restore "src/Hosts/Frappe.$host/Frappe.$host.csproj" -a "$TARGETARCH" -p:PublishReadyToRun=true --locked-mode || exit 1; \
    done

COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    for host in Api Worker Migrator; do \
      dotnet publish "src/Hosts/Frappe.$host/Frappe.$host.csproj" -c Release -a "$TARGETARCH" --no-restore \
        -p:PublishReadyToRun=true -p:DebugType=none -o "/app/$(echo $host | tr '[:upper:]' '[:lower:]')" || exit 1; \
    done

# The three hosts share most of their assemblies: identical files become hard links, so they are stored once.
WORKDIR /app
RUN for host in worker migrator; do \
      find "$host" -type f | while read -r file; do \
        shared="api/${file#*/}"; \
        if [ -f "$shared" ] && cmp -s "$file" "$shared"; then ln -f "$shared" "$file"; fi; \
      done; \
    done

# Ubuntu chiseled: no shell, no package manager, non-root. "extra" keeps ICU and tzdata, which the messages
# (es-AR, es-MX, pt-BR) and the holiday time zones need.
FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
# Owned by root and only readable by the app user: the process cannot rewrite its own binaries.
COPY --from=build /app/ ./
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
ENTRYPOINT ["/app/api/Frappe.Api"]
