#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

for ARG in "$@"; do
	ASF_ARGS+=" $ARG"
done

# We don't need our shell anymore, just replace the current process instead of starting a new one
exec dotnet ArchiSteamFarm.dll $ASF_ARGS
