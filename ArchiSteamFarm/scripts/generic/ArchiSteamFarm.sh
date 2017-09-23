#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"
dotnet ArchiSteamFarm.dll
