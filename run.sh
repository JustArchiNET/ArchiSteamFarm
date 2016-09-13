#!/bin/bash
set -eu

BUILD="Release"

UNTIL_CLEAN_EXIT=0

ASF_ARGS=("")

PRINT_USAGE() {
	echo "Usage: $0 [--until-clean-exit] [--cryptkey=] [--path=] [--server] [debug/release]"
	exit 1
}

for ARG in "$@"; do
	case "$ARG" in
		release|Release) BUILD="Release" ;;
		debug|Debug) BUILD="Debug" ;;
		--cryptkey=*) ASF_ARGS+=("$ARG") ;;
		--path=*) ASF_ARGS+=("$ARG") ;;
		--server) ASF_ARGS+=("$ARG") ;;
		--until-clean-exit) UNTIL_CLEAN_EXIT=1 ;;
		*) PRINT_USAGE
	esac
done

cd "$(dirname "$(readlink -f "$0")")"

if [[ -f "mono_envsetup.sh" ]]; then
	set +u
	source "mono_envsetup.sh"
	set -u
fi

BINARY="ArchiSteamFarm/bin/$BUILD/ArchiSteamFarm.exe"

if [[ ! -f "$BINARY" ]]; then
	echo "ERROR: $BINARY could not be found!"
	exit 1
fi

if [[ "$UNTIL_CLEAN_EXIT" -eq 0 ]]; then
	mono "$BINARY" "${ASF_ARGS[@]}"
	exit $?
fi

while [[ -f "$BINARY" ]]; do
	if mono "$BINARY" "${ASF_ARGS[@]}"; then
		break
	fi
	sleep 1
done
