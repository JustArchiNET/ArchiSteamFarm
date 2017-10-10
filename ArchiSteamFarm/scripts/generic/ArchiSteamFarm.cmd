@echo off
pushd %~dp0

SETLOCAL
SET ASF_ARGS=%ASF_ARGS% %*

dotnet ArchiSteamFarm.dll %ASF_ARGS%
