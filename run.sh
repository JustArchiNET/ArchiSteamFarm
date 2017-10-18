#!/bin/bash
set -eu

PROJECT="ArchiSteamFarm"
OUT="out/source"

cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

ASF_ARGS+=" $*"

# Kill underlying ASF process on shell process exit
trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM

if ! hash dotnet 2>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

if grep -Eq '"Headless":\s+?true' "${PROJECT}/${OUT}/config/ASF.json"; then
	# We're running ASF in headless mode so we don't need STDIN
	dotnet exec "${PROJECT}/${OUT}/${PROJECT}.dll" $ASF_ARGS & # Start ASF in the background, trap will work properly due to non-blocking call
	wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
else
	# We're running ASF in non-headless mode, so we need STDIN to be operative
	dotnet exec "${PROJECT}/${OUT}/${PROJECT}.dll" $ASF_ARGS # Start ASF in the foreground, trap won't work until process exit
fi
