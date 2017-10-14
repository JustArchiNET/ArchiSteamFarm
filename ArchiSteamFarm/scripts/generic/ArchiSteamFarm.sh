#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

ASF_ARGS+=" $*"

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT

dotnet ArchiSteamFarm.dll $ASF_ARGS & # We need to start ASF in the background for trap to work
wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
chmod +x "$0" # If ASF exited by itself, we need to ensure that our script is still set to +x after auto-update
