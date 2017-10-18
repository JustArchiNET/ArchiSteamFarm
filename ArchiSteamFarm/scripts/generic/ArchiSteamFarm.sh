#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

ASF_ARGS+=" $*"

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM

if grep -Eq '"Headless":\s+?true' 'config/ASF.json'; then
	# We're running ASF in headless mode so we don't need STDIN
	dotnet ArchiSteamFarm.dll $ASF_ARGS & # Start ASF in the background, trap will work properly due to non-blocking call
	wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
else
	# We're running ASF in non-headless mode, so we need STDIN to be operative
	dotnet ArchiSteamFarm.dll $ASF_ARGS # Start ASF in the foreground, trap won't work until process exit
fi

chmod +x "$0" # If ASF exited by itself, we need to ensure that our script is still set to +x after auto-update
