# The 10.0.1xx SDK matches the feature band global.json pins.
# BUILDPLATFORM: the framework-dependent output is architecture-neutral, so it never needs emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.103-noble@sha256:e362a8dbcd691522456da26a5198b8f3ca1d7641c95624fadc5e3e82678bd08a AS sdk
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    NUGET_PACKAGES=/nuget/packages
WORKDIR /source

# Only project and lock files, so the restore layer stays cached until a dependency changes.
FROM sdk AS project-files
COPY --link src/ src/
RUN find src -type f ! -name '*.csproj' ! -name 'packages.lock.json' -delete

FROM sdk AS build
COPY --link Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY --link --from=project-files /source/src/ src/
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet restore src/Frappe.Host/Frappe.Host.csproj --locked-mode

COPY --link .editorconfig ./
COPY --link eng/ eng/
COPY --link src/ src/
# No apphost, so one output runs on every architecture's runtime image.
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet publish src/Frappe.Host/Frappe.Host.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:UseAppHost=false \
        -p:ContinuousIntegrationBuild=true

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
