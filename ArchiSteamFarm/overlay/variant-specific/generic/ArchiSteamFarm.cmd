@echo off
pushd %~dp0

SETLOCAL
SET DOTNET_CLI_TELEMETRY_OPTOUT=true
SET DOTNET_EnableDiagnostics=0
SET DOTNET_NOLOGO=true

SET ASF_ARGS=%ASF_ARGS% %*

dotnet --info

dotnet ArchiSteamFarm.dll %ASF_ARGS%
