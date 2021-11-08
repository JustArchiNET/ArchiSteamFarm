ARG IMAGESUFFIX

FROM --platform=$BUILDPLATFORM node:lts${IMAGESUFFIX} AS build-node
WORKDIR /app
COPY ASF-ui .
RUN echo "node: $(node --version)" && \
    echo "npm: $(npm --version)" && \
    npm ci --no-progress && \
    npm run deploy --no-progress

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0${IMAGESUFFIX} AS build-dotnet
ARG CONFIGURATION=Release
ARG STEAM_TOKEN_DUMPER_TOKEN
ARG TARGETARCH
ARG TARGETOS
ENV DOTNET_CLI_TELEMETRY_OPTOUT true
ENV DOTNET_NOLOGO true
ENV NET_CORE_VERSION net6.0
ENV STEAM_TOKEN_DUMPER_NAME ArchiSteamFarm.OfficialPlugins.SteamTokenDumper
WORKDIR /app
COPY --from=build-node /app/dist ASF-ui/dist
COPY ArchiSteamFarm ArchiSteamFarm
COPY ArchiSteamFarm.OfficialPlugins.SteamTokenDumper ArchiSteamFarm.OfficialPlugins.SteamTokenDumper
COPY resources resources
COPY .editorconfig .editorconfig
COPY Directory.Build.props Directory.Build.props
COPY Directory.Packages.props Directory.Packages.props
COPY LICENSE-2.0.txt LICENSE-2.0.txt
RUN dotnet --info && \
    case "$TARGETOS" in \
      "linux") ;; \
      *) echo "ERROR: Unsupported OS: ${TARGETOS}"; exit 1 ;; \
    esac && \
    case "$TARGETARCH" in \
      "amd64") asf_variant="${TARGETOS}-x64" ;; \
      "arm") asf_variant="${TARGETOS}-${TARGETARCH}" ;; \
      "arm64") asf_variant="${TARGETOS}-${TARGETARCH}" ;; \
      *) echo "ERROR: Unsupported CPU architecture: ${TARGETARCH}"; exit 1 ;; \
    esac && \
    # TODO: Remove workaround for https://github.com/microsoft/msbuild/issues/3897 when it's no longer needed
    if [ -f "ArchiSteamFarm/Localization/Strings.zh-CN.resx" ]; then ln -s "Strings.zh-CN.resx" "ArchiSteamFarm/Localization/Strings.zh-Hans.resx"; fi && \
    if [ -f "ArchiSteamFarm/Localization/Strings.zh-TW.resx" ]; then ln -s "Strings.zh-TW.resx" "ArchiSteamFarm/Localization/Strings.zh-Hant.resx"; fi && \
    if [ -n "${STEAM_TOKEN_DUMPER_TOKEN-}" ] && [ -f "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs" ]; then sed -i "s/STEAM_TOKEN_DUMPER_TOKEN/${STEAM_TOKEN_DUMPER_TOKEN}/g" "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs"; dotnet publish "${STEAM_TOKEN_DUMPER_NAME}" -c "$CONFIGURATION" -f "$NET_CORE_VERSION" -o "out/${STEAM_TOKEN_DUMPER_NAME}/${NET_CORE_VERSION}" -p:ASFVariant=docker -p:ContinuousIntegrationBuild=true -p:SelfContained=false -p:UseAppHost=false -r "$asf_variant" --nologo; fi && \
    dotnet publish ArchiSteamFarm -c "$CONFIGURATION" -f "$NET_CORE_VERSION" -o "out/result" -p:ASFVariant=docker -p:ContinuousIntegrationBuild=true -p:SelfContained=false -p:UseAppHost=false -r "$asf_variant" --nologo && \
    if [ -d "ArchiSteamFarm/overlay/generic" ]; then cp -pR "ArchiSteamFarm/overlay/generic/"* "out/result"; fi && \
    if [ -d "out/${STEAM_TOKEN_DUMPER_NAME}/${NET_CORE_VERSION}" ]; then mkdir -p "out/result/plugins/${STEAM_TOKEN_DUMPER_NAME}"; cp -pR "out/${STEAM_TOKEN_DUMPER_NAME}/${NET_CORE_VERSION}/"* "out/result/plugins/${STEAM_TOKEN_DUMPER_NAME}"; fi

FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/aspnet:6.0${IMAGESUFFIX} AS runtime
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

RUN groupadd -r -g 1000 asf && \
    useradd -r -d /app -g 1000 -u 1000 asf && \
    chown -hR asf:asf /app

VOLUME ["/app/config", "/app/logs"]
HEALTHCHECK CMD ["pidof", "-q", "dotnet"]
ENTRYPOINT ["sh", "ArchiSteamFarm.sh", "--no-restart", "--process-required", "--system-required"]
