#!/bin/bash
set -eu

BUILD="Release"

MONO_ARGS=("--desktop" "--llvm" "-O=all")

PRINT_USAGE() {
	echo "Usage: $0 [debug/release]"
	exit 1
}

for ARG in "$@"; do
	case "$ARG" in
		release|Release) BUILD="Release" ;;
		debug|Debug) BUILD="Debug" ;;
		*) PRINT_USAGE
	esac
done

if [[ "$BUILD" = "Debug" ]]; then
	MONO_ARGS+=("--debug")
fi

cd "$(dirname "$(readlink -f "$0")")"

BINARY="ArchiSteamFarm/bin/$BUILD/ArchiSteamFarm.exe"

if [[ ! -f "$BINARY" ]]; then
	echo "ERROR: $BINARY could not be found!"
	exit 1
fi

mono "${MONO_ARGS[@]}" "$BINARY"
