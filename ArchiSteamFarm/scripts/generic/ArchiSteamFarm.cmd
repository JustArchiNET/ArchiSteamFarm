@echo off
pushd %~dp0

:loop
IF NOT "%1" == "" (
    SET ASF_ARGS=%ASF_ARGS% %1
    SHIFT
    GOTO :loop
)

dotnet ArchiSteamFarm.dll %ASF_ARGS%
