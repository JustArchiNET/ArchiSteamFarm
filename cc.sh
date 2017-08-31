#!/bin/bash
set -eu

PROJECT="ArchiSteamFarm"
OUT="out/source"

SOLUTION="${PROJECT}.sln"
CONFIGURATION="Release"

CLEAN=0

PRINT_USAGE() {
	echo "Usage: $0 [--clean] [debug/release]"
	exit 1
}

for ARG in "$@"; do
	case "$ARG" in
		release|Release) CONFIGURATION="Release" ;;
		debug|Debug) CONFIGURATION="Debug" ;;
		--clean) CLEAN=1 ;;
		*) PRINT_USAGE
	esac
done

if ! hash dotnet &>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

cd "$(dirname "$(readlink -f "$0")")"

if [[ -d ".git" ]] && hash git &>/dev/null; then
	git pull || true
fi

if [[ ! -f "$SOLUTION" ]]; then
	echo "ERROR: $SOLUTION could not be found!"
	exit 1
fi

if [[ "$CLEAN" -eq 1 ]]; then
	dotnet clean -c "$CONFIGURATION" -o "$OUT"
	rm -rf "ArchiSteamFarm/${OUT}" "ArchiSteamFarm.Tests/${OUT}"
fi

dotnet restore

dotnet build -c "$CONFIGURATION" -o "$OUT" --no-restore /nologo
dotnet test ArchiSteamFarm.Tests -c "$CONFIGURATION" -o "$OUT" --no-build --no-restore

echo
echo "Compilation finished successfully! :)"
