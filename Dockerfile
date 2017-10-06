FROM microsoft/dotnet:2.0-sdk AS build-env
WORKDIR /app

COPY . ./
RUN dotnet publish ArchiSteamFarm -c Release -o out /nologo

FROM microsoft/dotnet:2.0-runtime 
WORKDIR /app
COPY --from=build-env /app/out ./
ENTRYPOINT ["dotnet", "ArchiSteamFarm.dll"]
