FROM node:lts AS build-node
WORKDIR /app
COPY ASF-ui .
RUN echo "node: $(node --version)" && \
    echo "npm: $(npm --version)" && \
    npm ci && \
    npm run-script deploy

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-dotnet
ENV CONFIGURATION Release
ENV NET_CORE_VERSION netcoreapp3.1
WORKDIR /app
COPY --from=build-node /app/dist ASF-ui/dist
COPY ArchiSteamFarm ArchiSteamFarm
COPY ArchiSteamFarm.Tests ArchiSteamFarm.Tests
RUN dotnet --info && \
    # TODO: Remove workaround for https://github.com/microsoft/msbuild/issues/3897 when it's no longer needed
    if [ -f "ArchiSteamFarm/Localization/Strings.zh-CN.resx" ]; then ln -s "Strings.zh-CN.resx" "ArchiSteamFarm/Localization/Strings.zh-Hans.resx"; fi && \
    if [ -f "ArchiSteamFarm/Localization/Strings.zh-TW.resx" ]; then ln -s "Strings.zh-TW.resx" "ArchiSteamFarm/Localization/Strings.zh-Hant.resx"; fi && \
    dotnet build ArchiSteamFarm -c "$CONFIGURATION" -f "$NET_CORE_VERSION" --nologo && \
    dotnet test ArchiSteamFarm.Tests -c "$CONFIGURATION" -f "$NET_CORE_VERSION" --nologo && \
    dotnet clean ArchiSteamFarm -c "$CONFIGURATION" -f "$NET_CORE_VERSION" --nologo && \
    dotnet publish ArchiSteamFarm -c "$CONFIGURATION" -f "$NET_CORE_VERSION" -o 'out' --nologo /p:ASFVariant=docker /p:UseAppHost=false && \
    cp "ArchiSteamFarm/overlay/generic/ArchiSteamFarm.sh" "out/ArchiSteamFarm.sh"

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim-arm32v7 AS runtime
ENV ASPNETCORE_URLS=
LABEL maintainer="JustArchi <JustArchi@JustArchi.net>"
EXPOSE 1242
WORKDIR /app
COPY --from=build-dotnet /app/out .
ENTRYPOINT ["./ArchiSteamFarm.sh", "--no-restart", "--process-required", "--system-required"]
