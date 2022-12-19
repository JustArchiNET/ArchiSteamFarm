ARG IMAGESUFFIX

FROM --platform=$BUILDPLATFORM node:lts${IMAGESUFFIX} AS build-node
WORKDIR /app/ASF-ui
COPY ASF-ui .
COPY .git/modules/ASF-ui /app/.git/modules/ASF-ui
RUN set -eu; \
    echo "node: $(node --version)"; \
    echo "npm: $(npm --version)"; \
    npm ci --no-progress; \
    npm run deploy --no-progress

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:7.0${IMAGESUFFIX} AS build-dotnet
ARG CONFIGURATION=Release
ARG STEAM_TOKEN_DUMPER_TOKEN
ARG TARGETARCH
ARG TARGETOS
ENV DOTNET_CLI_TELEMETRY_OPTOUT true
ENV DOTNET_NOLOGO true
ENV NET_CORE_VERSION net7.0
ENV PLUGINS ArchiSteamFarm.OfficialPlugins.ItemsMatcher ArchiSteamFarm.OfficialPlugins.SteamTokenDumper
WORKDIR /app
COPY --from=build-node /app/ASF-ui/dist ASF-ui/dist
COPY ArchiSteamFarm ArchiSteamFarm
COPY ArchiSteamFarm.OfficialPlugins.ItemsMatcher ArchiSteamFarm.OfficialPlugins.ItemsMatcher
COPY ArchiSteamFarm.OfficialPlugins.SteamTokenDumper ArchiSteamFarm.OfficialPlugins.SteamTokenDumper
COPY resources resources
COPY .editorconfig .editorconfig
COPY Directory.Build.props Directory.Build.props
COPY Directory.Packages.props Directory.Packages.props
COPY LICENSE.txt LICENSE.txt
RUN set -eu; \
    dotnet --info; \
    \
    case "$TARGETOS" in \
      "linux") ;; \
      *) echo "ERROR: Unsupported OS: ${TARGETOS}"; exit 1 ;; \
    esac; \
    \
    case "$TARGETARCH" in \
      "amd64") asf_variant="${TARGETOS}-x64" ;; \
      "arm") asf_variant="${TARGETOS}-${TARGETARCH}" ;; \
      "arm64") asf_variant="${TARGETOS}-${TARGETARCH}" ;; \
      *) echo "ERROR: Unsupported CPU architecture: ${TARGETARCH}"; exit 1 ;; \
    esac; \
    \
    dotnet publish ArchiSteamFarm -c "$CONFIGURATION" -f "$NET_CORE_VERSION" -o "out/result" -p:ASFVariant=docker -p:ContinuousIntegrationBuild=true -p:UseAppHost=false -r "$asf_variant" --nologo --no-self-contained; \
    \
    if [ -n "${STEAM_TOKEN_DUMPER_TOKEN-}" ] && [ -f "ArchiSteamFarm.OfficialPlugins.SteamTokenDumper/SharedInfo.cs" ]; then \
      sed -i "s/STEAM_TOKEN_DUMPER_TOKEN/${STEAM_TOKEN_DUMPER_TOKEN}/g" "ArchiSteamFarm.OfficialPlugins.SteamTokenDumper/SharedInfo.cs"; \
    fi; \
    \
    for plugin in $PLUGINS; do \
      dotnet publish "$plugin" -c "$CONFIGURATION" -f "$NET_CORE_VERSION" -o "out/result/plugins/$plugin" -p:ASFVariant=docker -p:ContinuousIntegrationBuild=true -p:UseAppHost=false -r "$asf_variant" --nologo --no-self-contained; \
    done

FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/aspnet:7.0${IMAGESUFFIX} AS runtime
ENV ASF_USER asf
ENV ASPNETCORE_URLS=
ENV DOTNET_CLI_TELEMETRY_OPTOUT true
ENV DOTNET_NOLOGO true

LABEL maintainer="JustArchi <JustArchi@JustArchi.net>" \
    org.opencontainers.image.authors="JustArchi <JustArchi@JustArchi.net>" \
    org.opencontainers.image.url="https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Docker" \
    org.opencontainers.image.documentation="https://github.com/JustArchiNET/ArchiSteamFarm/wiki" \
    org.opencontainers.image.source="https://github.com/JustArchiNET/ArchiSteamFarm" \
    org.opencontainers.image.vendor="JustArchiNET" \
    org.opencontainers.image.licenses="Apache-2.0" \
    org.opencontainers.image.title="ArchiSteamFarm" \
    org.opencontainers.image.description="C# application with primary purpose of idling Steam cards from multiple accounts simultaneously"

EXPOSE 1242
WORKDIR /app
COPY --from=build-dotnet /app/out/result .

RUN set -eu; \
    groupadd -r -g 1000 asf; \
    useradd -r -d /app -g 1000 -u 1000 asf; \
    chown -hR asf:asf /app

VOLUME ["/app/config", "/app/logs"]
HEALTHCHECK CMD ["pidof", "-q", "dotnet"]
ENTRYPOINT ["sh", "ArchiSteamFarm.sh", "--no-restart", "--process-required", "--system-required"]
