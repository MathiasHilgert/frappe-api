# The SDK image matches the feature band global.json pins; the runtime image is patched independently.
# The build runs on the build machine's architecture: the framework-dependent output is
# architecture-neutral IL, so one publish serves every target platform without emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.103-noble@sha256:e362a8dbcd691522456da26a5198b8f3ca1d7641c95624fadc5e3e82678bd08a AS sdk
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    NUGET_PACKAGES=/nuget/packages
WORKDIR /source

# Keep only the files restore reads, so the restore layer is reused until a dependency changes.
FROM sdk AS manifests
COPY src/ src/
RUN find src -type f ! -name '*.csproj' ! -name 'packages.lock.json' -delete

FROM sdk AS publish
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY --from=manifests /source/src/ src/
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet restore src/Frappe.Host/Frappe.Host.csproj --locked-mode
COPY .editorconfig ./
COPY eng/ eng/
COPY src/ src/
# No apphost: the output stays architecture-neutral and the runtime image's dotnet runs it.
RUN --mount=type=cache,id=nuget,target=/nuget/packages \
    dotnet publish src/Frappe.Host/Frappe.Host.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:UseAppHost=false \
        -p:ContinuousIntegrationBuild=true

# Chiseled: no shell, no package manager, non-root by default. The -extra variant carries ICU and
# tzdata, which culture-aware formatting (es, en, pt) and time-zone conversion need.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8 AS runtime
ARG REVISION=unknown
LABEL org.opencontainers.image.title="frappe-api" \
      org.opencontainers.image.description="Frappé backend host; Platform__Role selects all, api, worker or scheduler" \
      org.opencontainers.image.source="https://github.com/MathiasHilgert/frappe-api" \
      org.opencontainers.image.licenses="LicenseRef-Proprietary" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.base.name="mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra"
# Restated from the base image so the listening port is part of this image's contract.
ENV ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
# Owned by root, so the app user cannot modify its own binaries.
COPY --link --from=publish /app ./
# The base image's non-root app user (APP_UID), numeric so runAsNonRoot checks can verify it.
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Frappe.Host.dll"]
