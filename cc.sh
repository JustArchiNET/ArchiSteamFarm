#!/usr/bin/env sh
set -eu

TARGET_FRAMEWORK="netcoreapp3.1"

MAIN_PROJECT="ArchiSteamFarm"
STEAM_TOKEN_DUMPER_NAME="${MAIN_PROJECT}.OfficialPlugins.SteamTokenDumper"
TESTS_PROJECT="${MAIN_PROJECT}.Tests"
SOLUTION="${MAIN_PROJECT}.sln"
CONFIGURATION="Release"
OUT="out"
OUT_ASF="${OUT}/result"
OUT_STD="${OUT}/${STEAM_TOKEN_DUMPER_NAME}"

ASF_UI=1
CLEAN=0
PULL=1
SHARED_COMPILATION=1
TEST=1

PRINT_USAGE() {
	echo "Usage: $0 [--clean] [--no-asf-ui] [--no-pull] [--no-shared-compilation] [--no-test] [debug/release]"
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

trap "trap - TERM && kill -- -$$" INT TERM

if ! command -v dotnet >/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

if [ "$PULL" -eq 1 ] && [ -d ".git" ] && command -v git >/dev/null; then
	git pull --recurse-submodules=on-demand || true
fi

if [ ! -f "$SOLUTION" ]; then
	echo "ERROR: $SOLUTION could not be found!"
	exit 1
fi

os_type="$(uname -s)"

case "$os_type" in
	"Darwin") os_type="osx" ;;
	"Linux") os_type="linux" ;;
	*) echo "ERROR: Unknown OS type: ${os_type}. If you believe that our script should work on your machine, please let us know."; exit 1
esac

cpu_architecture="$(uname -m)"

case "$cpu_architecture" in
	"aarch64") cpu_architecture="arm64" ;;
	"armv7l") cpu_architecture="arm" ;;
	"x86_64") cpu_architecture="x64" ;;
	*) echo "ERROR: Unknown CPU architecture: ${cpu_architecture}. If you believe that our script should work on your machine, please let us know."; exit 1
esac

echo "INFO: Detected ${os_type}-${cpu_architecture} machine."

if [ "$ASF_UI" -eq 1 ]; then
	if [ -f "ASF-ui/package.json" ] && command -v npm >/dev/null; then
		echo "INFO: Building ASF-ui..."

		# ASF-ui doesn't clean itself after old build
		rm -rf "ASF-ui/dist"

		npm ci --no-progress --prefix ASF-ui
		npm run-script deploy --no-progress --prefix ASF-ui

		# ASF's output www folder needs cleaning as well
		rm -rf "${OUT_ASF}/www"
	else
		echo "WARNING: ASF-ui dependencies are missing, skipping build of ASF-ui..."
	fi
fi

DOTNET_FLAGS="-c $CONFIGURATION -f $TARGET_FRAMEWORK -p:SelfContained=false -p:UseAppHost=false -r ${os_type}-${cpu_architecture} --nologo"

if [ "$SHARED_COMPILATION" -eq 0 ]; then
	DOTNET_FLAGS="$DOTNET_FLAGS -p:UseSharedCompilation=false"
fi

if [ "$CLEAN" -eq 1 ]; then
	dotnet clean $DOTNET_FLAGS
	rm -rf "$OUT"
fi

if [ "$TEST" -eq 1 ]; then
	dotnet test "$TESTS_PROJECT" $DOTNET_FLAGS
fi

dotnet publish "$MAIN_PROJECT" -o "$OUT_ASF" $DOTNET_FLAGS

if [ -n "${STEAM_TOKEN_DUMPER_TOKEN-}" ] && [ -f "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs" ]; then
	git checkout -- "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs"
	sed "s/STEAM_TOKEN_DUMPER_TOKEN/${STEAM_TOKEN_DUMPER_TOKEN}/g" "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs" > "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs.new";
	mv "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs.new" "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs"

	dotnet publish "$STEAM_TOKEN_DUMPER_NAME" -o "$OUT_STD" $DOTNET_FLAGS
	git checkout -- "${STEAM_TOKEN_DUMPER_NAME}/SharedInfo.cs"

	mkdir -p "${OUT_ASF}/plugins/${STEAM_TOKEN_DUMPER_NAME}"
	cp "${OUT_STD}/${STEAM_TOKEN_DUMPER_NAME}.dll" "${OUT_ASF}/plugins/${STEAM_TOKEN_DUMPER_NAME}"
else
	echo "WARNING: STEAM_TOKEN_DUMPER_TOKEN is missing, skipping build of ${STEAM_TOKEN_DUMPER_NAME}..."
fi

echo
echo "SUCCESS: Compilation finished successfully! :)"
