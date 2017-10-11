FROM microsoft/dotnet:2.0-sdk AS build-env
WORKDIR /app
COPY . ./
RUN dotnet publish ArchiSteamFarm -c Release -o out /nologo && \
    cp "ArchiSteamFarm/scripts/generic/ArchiSteamFarm.sh" "ArchiSteamFarm/out/ArchiSteamFarm.sh"

FROM microsoft/dotnet:2.0-runtime
WORKDIR /app
COPY --from=build-env /app/ArchiSteamFarm/out ./
ENTRYPOINT ["./ArchiSteamFarm.sh", "--service"]
