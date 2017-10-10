#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

ASF_ARGS+=" $*"

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT

while [[ -f ArchiSteamFarm.dll ]]; do
	dotnet ArchiSteamFarm.dll $ASF_ARGS &
	wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
	sleep 1
done
