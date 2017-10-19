#!/bin/bash
set -eu

PROJECT="ArchiSteamFarm"
OUT="out/source"

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

if ! hash dotnet 2>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

cd "${PROJECT}/${OUT}"

if [[ -f "$CONFIG_PATH" ]] && grep -Eq '"Headless":\s+?true' "$CONFIG_PATH"; then
	# We're running ASF in headless mode so we don't need STDIN
	dotnet exec "${PROJECT}.dll" $ASF_ARGS & # Start ASF in the background, trap will work properly due to non-blocking call
	wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
else
	# We're running ASF in non-headless mode, so we need STDIN to be operative
	dotnet exec "${PROJECT}.dll" $ASF_ARGS # Start ASF in the foreground, trap won't work until process exit
fi
