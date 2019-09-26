#!/usr/bin/env bash
set -euo pipefail

TARGET_FRAMEWORK="netcoreapp3.0"

MAIN_PROJECT="ArchiSteamFarm"
TESTS_PROJECT="${MAIN_PROJECT}.Tests"
SOLUTION="${MAIN_PROJECT}.sln"
CONFIGURATION="Release"
OUT="out"

ASF_UI=1
CLEAN=0
PUBLISH_TRIMMED=0
PULL=1
SHARED_COMPILATION=1
TEST=1

PRINT_USAGE() {
	echo "Usage: $0 [--clean] [--publish-trimmed] [--no-asf-ui] [--no-pull] [--no-shared-compilation] [--no-test] [debug/release]"
}

cd "$(dirname "$(readlink -f "$0")")"

for ARG in "$@"; do
	case "$ARG" in
		debug|Debug) CONFIGURATION="Debug" ;;
		release|Release) CONFIGURATION="Release" ;;
		--asf-ui) ASF_UI=1 ;;
		--no-asf-ui) ASF_UI=0 ;;
		--clean) CLEAN=1 ;;
		--no-clean) CLEAN=0 ;;
		--publish-trimmed) PUBLISH_TRIMMED=1 ;;
		--no-publish-trimmed) PUBLISH_TRIMMED=0 ;;
		--pull) PULL=1 ;;
		--no-pull) PULL=0 ;;
		--shared-compilation) SHARED_COMPILATION=1 ;;
		--no-shared-compilation) SHARED_COMPILATION=0 ;;
		--test) TEST=1 ;;
		--no-test) TEST=0 ;;
		--help) PRINT_USAGE; exit 0 ;;
		*) PRINT_USAGE; exit 1
	esac
done

trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM

if ! hash dotnet 2>/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

if [[ "$PULL" -eq 1 && -d ".git" ]] && hash git 2>/dev/null; then
	git pull --recurse-submodules=on-demand || true
fi

if [[ ! -f "$SOLUTION" ]]; then
	echo "ERROR: $SOLUTION could not be found!"
	exit 1
fi

if [[ "$ASF_UI" -eq 1 ]]; then
	if [[ -f "ASF-ui/package.json" ]] && hash npm 2>/dev/null; then
		echo "Building ASF-ui..."

		# ASF-ui doesn't clean itself after old build
		rm -rf "ASF-ui/dist"

		(
			cd ASF-ui
			npm ci
			npm run-script deploy
		)

		# ASF's output www folder needs cleaning as well
		rm -rf "${OUT}/www"
	else
		echo "WARNING: ASF-ui dependencies are missing, skipping build of ASF-ui..."
	fi
fi

DOTNET_FLAGS=(-c "$CONFIGURATION" -f "$TARGET_FRAMEWORK" '/nologo')

if [[ "$PUBLISH_TRIMMED" -eq 0 ]]; then
	DOTNET_FLAGS+=('/p:PublishTrimmed=false')
fi

if [[ "$SHARED_COMPILATION" -eq 0 ]]; then
	DOTNET_FLAGS+=('/p:UseSharedCompilation=false')
fi

if [[ "$CLEAN" -eq 1 ]]; then
	dotnet clean "${DOTNET_FLAGS[@]}"
	rm -rf "$OUT"
fi

if [[ "$TEST" -eq 1 ]]; then
	dotnet test "$TESTS_PROJECT" "${DOTNET_FLAGS[@]}"
fi

dotnet publish "$MAIN_PROJECT" "${DOTNET_FLAGS[@]}" -o "$OUT"

echo
echo "SUCCESS: Compilation finished successfully! :)"
