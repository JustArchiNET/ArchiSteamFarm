#!/bin/bash
set -eu

BUILD="Release"
AOT=0
CLEAN=0

XBUILD_ARGS=("/nologo")
BINARIES=("ArchiSteamFarm/bin/Release/ArchiSteamFarm.exe")
SOLUTION="ArchiSteamFarm.sln"

PRINT_USAGE() {
	echo "Usage: $0 [--clean] [--aot] [debug/release]"
	exit 1
}

for ARG in "$@"; do
	case "$ARG" in
		release|Release) BUILD="Release" ;;
		debug|Debug) BUILD="Debug" ;;
		--aot) AOT=1 ;;
		--clean) CLEAN=1 ;;
		*) PRINT_USAGE
	esac
done

XBUILD_ARGS+=("/p:Configuration=$BUILD")

cd "$(dirname "$(readlink -f "$0")")"

if [[ -f "mono_envsetup.sh" ]]; then
	set +u
	source "mono_envsetup.sh"
	set -u
fi

if [[ -d ".git" ]] && hash git &>/dev/null; then
	git pull || true
fi

if [[ ! -f "$SOLUTION" ]]; then
	echo "ERROR: $SOLUTION could not be found!"
	exit 1
fi

if hash nuget &>/dev/null; then
	nuget restore "$SOLUTION" || true
fi

if [[ "$CLEAN" -eq 1 ]]; then
	rm -rf out
	xbuild "${XBUILD_ARGS[@]}" "/t:Clean" "$SOLUTION"
fi

xbuild "${XBUILD_ARGS[@]}" "$SOLUTION"

if [[ ! -f "${BINARIES[0]}" ]]; then
	echo "ERROR: ${BINARIES[0]} binary could not be found!"
fi

# Use Mono AOT for output binaries if needed
if [[ "$AOT" -eq 1 && "$BUILD" = "Release" ]]; then
	for BINARY in "${BINARIES[@]}"; do
		if [[ ! -f "$BINARY" ]]; then
			continue
		fi

		mono --aot "$BINARY"
	done
fi

echo
echo "Compilation finished successfully! :)"
