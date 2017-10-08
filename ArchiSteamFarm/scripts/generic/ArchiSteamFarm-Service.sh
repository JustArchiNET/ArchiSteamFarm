#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

for ARG in "$@"; do
	ASF_ARGS+=" $ARG"
done

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT

while [[ -f ArchiSteamFarm.dll ]]; do
	dotnet ArchiSteamFarm.dll $ASF_ARGS # We will abort the script if ASF exits with an error
	sleep 1
done
