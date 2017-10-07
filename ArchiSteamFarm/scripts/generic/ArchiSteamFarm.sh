#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

# We don't need our shell anymore, just replace the current process instead of starting a new one
exec dotnet $ASF_ARGS ArchiSteamFarm.dll
