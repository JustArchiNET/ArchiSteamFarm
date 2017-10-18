#!/bin/bash
set -eu

SOLUTION="ArchiSteamFarm.sln"
CONFIGURATION="Release"
OUT="out/source"

PROJECTS=("ArchiSteamFarm")

CLEAN=0
TEST=1

cd "$(dirname "$(readlink -f "$0")")"

for ARG in "$@"; do
	case "$ARG" in
		release|Release) CONFIGURATION="Release" ;;
		debug|Debug) CONFIGURATION="Debug" ;;
		--clean) CLEAN=1 ;;
		--no-test) TEST=0 ;;
		*) echo "Usage: $0 [--clean] [--no-test] [debug/release]"; exit 1
	esac
done

if [[ "$TEST" -eq 1 ]]; then
	PROJECTS+=("ArchiSteamFarm.Tests")
fi

trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM

if ! hash dotnet 2>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

if [[ -d ".git" ]] && hash git 2>/dev/null; then
	git pull || true
fi

if [[ ! -f "$SOLUTION" ]]; then
	echo "ERROR: $SOLUTION could not be found!"
	exit 1
fi

if [[ "$CLEAN" -eq 1 ]]; then
	dotnet clean "${PROJECTS[@]}" -c "$CONFIGURATION" -o "$OUT"

	for PROJECT in "${PROJECTS[@]}"; do
		rm -rf "${PROJECT:?}/${OUT}"
	done
fi

dotnet restore
dotnet build "${PROJECTS[@]}" -c "$CONFIGURATION" -o "$OUT" --no-restore /nologo

if [[ "$TEST" -eq 1 ]]; then
	dotnet test ArchiSteamFarm.Tests -c "$CONFIGURATION" -o "$OUT" --no-build --no-restore
fi

echo
echo "Compilation finished successfully! :)"
