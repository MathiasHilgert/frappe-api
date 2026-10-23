# syntax=docker/dockerfile:1.20
# Build from the repo root: docker build -f deploy/Dockerfile .
# Multi-arch: docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile .

# ---- restore: only what affects restore, so source edits keep this layer cached ----
# The SDK runs natively on the build host; TARGETARCH selects the output RID.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
ARG TARGETARCH
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY eng/ eng/
# --parents keeps each csproj's path, so new projects are picked up without editing this file.
COPY --parents src/**/*.csproj ./
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Host/Frappe.Api/Frappe.Api.csproj -a "$TARGETARCH"

# ---- publish ----
FROM restore AS publish
ARG TARGETARCH
COPY src/ src/
# Framework-dependent, untrimmed: Wolverine compiles handlers at runtime and EF Core relies on reflection.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Host/Frappe.Api/Frappe.Api.csproj \
      -c Release -a "$TARGETARCH" --no-restore --self-contained false \
      -p:UseAppHost=false -o /app

# ---- final: chiseled (distroless, non-root); -extra ships ICU + tzdata for real cultures ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
ARG REVISION=unknown
ARG CREATED=unknown
LABEL org.opencontainers.image.title="frappe-api" \
      org.opencontainers.image.description="Frappé API" \
      org.opencontainers.image.vendor="NULLED SOFTWARE" \
      org.opencontainers.image.licenses="LicenseRef-Proprietary" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.created="$CREATED"
WORKDIR /app
COPY --link --from=publish /app .
USER $APP_UID
EXPOSE 8080
# No HEALTHCHECK: chiseled has no shell/curl; the orchestrator probes /health/live and /health/ready.
# No arguments runs the API. Before starting a new version, the deploy runs the same image once with
# `resources setup` (and the connection strings) to create the Wolverine message storage schema:
#   docker run --rm -e ConnectionStrings__frappe=... <image> resources setup
ENTRYPOINT ["dotnet", "Frappe.Api.dll"]
