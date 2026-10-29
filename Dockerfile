# The newest SDK band; global.json's latestFeature accepts it and any local 10.0 SDK from 10.0.100 up.
# BUILDPLATFORM: the framework-dependent output is architecture-neutral, so it never needs emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS sdk
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    NUGET_PACKAGES=/nuget/packages
WORKDIR /source

# Only project, lock and subtree Directory.Build.props files, so the restore layer stays cached until a dependency changes.
FROM sdk AS project-files
COPY --link src/ src/
RUN find src -type f ! -name '*.csproj' ! -name 'packages.lock.json' ! -name 'Directory.Build.props' -delete

FROM sdk AS sources
COPY --link Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY --link .config/ .config/
COPY --link --from=project-files /source/src/ src/
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet restore src/Frappe.Host/Frappe.Host.csproj --locked-mode \
    && dotnet restore src/Frappe.Migrations/Frappe.Migrations.csproj --locked-mode

COPY --link .editorconfig ./
COPY --link eng/ eng/
COPY --link src/ src/

FROM sources AS build
# The commit goes into the informational version (1.0.0+<commit>), which telemetry reports as service.version.
ARG REVISION=unknown
# No apphost, so one output runs on every architecture's runtime image.
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet publish src/Frappe.Host/Frappe.Host.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:UseAppHost=false \
        -p:ContinuousIntegrationBuild=true \
        -p:SourceRevisionId="${REVISION}"

# The migrator: applies every context's pending migrations, in order, given --connection.
FROM sources AS migrations-build
# Framework-dependent on the host's own base image: Persistence pulls in the ASP.NET shared framework
# through Health, and every base layer is shared with the host image.
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet publish src/Frappe.Migrations/Frappe.Migrations.csproj \
        --configuration Release \
        --no-restore \
        --output /migrations \
        -p:UseAppHost=false \
        -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8 AS migrations
ARG REVISION=unknown
LABEL org.opencontainers.image.title="frappe-migrations" \
      org.opencontainers.image.description="Applies pending Frappé database migrations; run with --connection and the owner role" \
      org.opencontainers.image.source="https://github.com/MathiasHilgert/frappe-api" \
      org.opencontainers.image.licenses="LicenseRef-Proprietary" \
      org.opencontainers.image.revision="${REVISION}"
WORKDIR /app
COPY --link --from=migrations-build /migrations ./
USER 1654
ENTRYPOINT ["dotnet", "Frappe.Migrations.dll"]

# The host image stays last, so a build without --target produces it.
# Chiseled (no shell, no package manager); -extra adds the ICU and tzdata that culture-aware formatting needs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8 AS final
ARG REVISION=unknown
LABEL org.opencontainers.image.title="frappe-api" \
      org.opencontainers.image.description="Frappé backend host; Platform__Role selects all, api, worker or scheduler" \
      org.opencontainers.image.source="https://github.com/MathiasHilgert/frappe-api" \
      org.opencontainers.image.licenses="LicenseRef-Proprietary" \
      org.opencontainers.image.revision="${REVISION}"
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app ./
# The base image's app user, numeric so runAsNonRoot can verify it; the root-owned binaries stay read-only to it.
USER 1654
ENTRYPOINT ["dotnet", "Frappe.Host.dll"]
