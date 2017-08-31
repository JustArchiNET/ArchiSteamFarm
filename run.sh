#!/bin/bash
set -eu

PROJECT="ArchiSteamFarm"
OUT="out/source"

BINARY="${PROJECT}/${OUT}/${PROJECT}.dll"

ASF_ARGS=("")
UNTIL_CLEAN_EXIT=0

PRINT_USAGE() {
	echo "Usage: $0 [--until-clean-exit] [--cryptkey=] [--path=] [--server]"
	exit 1
}

for ARG in "$@"; do
	case "$ARG" in
		--cryptkey=*) ASF_ARGS+=("$ARG") ;;
		--path=*) ASF_ARGS+=("$ARG") ;;
		--server) ASF_ARGS+=("$ARG") ;;
		--until-clean-exit) UNTIL_CLEAN_EXIT=1 ;;
		*) PRINT_USAGE
	esac
done

if ! hash dotnet &>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

cd "$(dirname "$(readlink -f "$0")")"

if [[ ! -f "$BINARY" ]]; then
	echo "ERROR: $BINARY could not be found!"
	exit 1
fi

if [[ "$UNTIL_CLEAN_EXIT" -eq 0 ]]; then
	dotnet exec "$BINARY" "${ASF_ARGS[@]}"
	exit $? # In this case $? can only be 0 because otherwise set -e terminates the script
fi

while [[ -f "$BINARY" ]]; do
	if dotnet exec "$BINARY" "${ASF_ARGS[@]}"; then
		break
	fi

	sleep 1
done
