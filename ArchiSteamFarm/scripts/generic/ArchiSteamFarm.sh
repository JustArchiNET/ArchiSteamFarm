#!/bin/bash
set -eu

CONFIG_PATH="config/ASF.json"

cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

ASF_ARGS+=" $*"

for ARG in $ASF_ARGS; do
	case "$ARG" in
		--path=*) CONFIG_PATH="$(echo "$ARG" | cut -d '=' -f 2-)/${CONFIG_PATH}" ;;
	esac
done

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM

if [[ -f "$CONFIG_PATH" ]] && grep -Eq '"Headless":\s+?true' "$CONFIG_PATH"; then
	# We're running ASF in headless mode so we don't need STDIN
	dotnet ArchiSteamFarm.dll $ASF_ARGS & # Start ASF in the background, trap will work properly due to non-blocking call
	wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
else
	# We're running ASF in non-headless mode, so we need STDIN to be operative
	dotnet ArchiSteamFarm.dll $ASF_ARGS # Start ASF in the foreground, trap won't work until process exit
fi

chmod +x "$0" # If ASF exited by itself, we need to ensure that our script is still set to +x after auto-update
