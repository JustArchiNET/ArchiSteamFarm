#!/usr/bin/env sh
set -eu

export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_EnableDiagnostics=0
export DOTNET_NOLOGO=true

CONFIG_PATH="config/ASF.json"
OS_TYPE="$(uname -s)"

case "$OS_TYPE" in
	"Darwin") SCRIPT_PATH="$(readlink "$0")" ;;
	"FreeBSD") SCRIPT_PATH="$(readlink -f "$0")" ;;
	"Linux") SCRIPT_PATH="$(readlink -f "$0")" ;;
	*) echo "ERROR: Unknown OS type: ${OS_TYPE}. If you believe that our script should work on your machine, please let us know."; exit 1
esac

SCRIPT_DIR="$(dirname "$SCRIPT_PATH")"
BINARY="${SCRIPT_DIR}/ArchiSteamFarm.dll"

if [ ! -f "$BINARY" ]; then
	echo "ERROR: $BINARY could not be found!"
	exit 1
fi

cd "$SCRIPT_DIR"

PATH_NEXT=0
SERVICE=0

PARSE_ARG() {
	case "$1" in
		--path) PATH_NEXT=1 ;;
		--path=*)
			if [ "$PATH_NEXT" -eq 1 ]; then
				PATH_NEXT=0
				cd "$1"
			else
				cd "$(echo "$1" | cut -d '=' -f 2-)"
			fi
			;;
		--service) SERVICE=1 ;;
		*)
			if [ "$PATH_NEXT" -eq 1 ]; then
				PATH_NEXT=0
				cd "$1"
			fi
	esac
}

if [ -n "${ASF_PATH-}" ]; then
	cd "$ASF_PATH"
fi

if [ -n "${ASF_ARGS-}" ]; then
	for ARG in $ASF_ARGS; do
		if [ -n "$ARG" ]; then
			PARSE_ARG "$ARG"
		fi
	done
fi

for ARG in "$@"; do
	if [ -n "$ARG" ]; then
		PARSE_ARG "$ARG"
	fi
done

BINARY_PREFIX=""

if [ -n "${ASF_UID-}" ]; then
	# ASF_UID specified, what can we do about it?
	SCRIPT_UID="$(id -u)"

	if [ "$SCRIPT_UID" -eq 0 ]; then
		# We're running as root, so we can fix everything

		# Create missing group if needed
		if ! getent group "$ASF_UID" > /dev/null; then
			groupadd -r -g "$ASF_UID" "asf-${ASF_UID}"
		fi

		# Create missing user if needed
		if ! getent passwd "$ASF_UID" > /dev/null; then
			useradd -r -d "$(pwd)" -g "$ASF_UID" -u "$ASF_UID" "asf-${ASF_UID}"
		fi

		# Is target different than root?
		if [ "$(id -u "$ASF_UID")" -gt 0 ]; then
			# Fix permissions to ensure ASF has read/write access to the directory specified by --path and its own
			chown -hR "${ASF_UID}:${ASF_UID}" . "$SCRIPT_DIR" || true

			# And finally run as target user
			BINARY_PREFIX="su $(id -nu "$ASF_UID") -c"
		fi
	else
		# We're not running as root, we have to rely on user doing things right
		if [ "$(stat -L -c '%u' "$SCRIPT_DIR")" -ne "$SCRIPT_UID" ]; then
			echo "WARNING: Current user ${SCRIPT_UID} does NOT have access to ${SCRIPT_DIR} - fix that ASAP, as you'll run into permission problems soon enough!"
		fi

		UID_HOME="$(pwd)"

		# Does current directory belong to us at least?
		if [ "$(stat -L -c '%u' "$UID_HOME")" -ne "$SCRIPT_UID" ]; then
			# Don't warn about the same directory twice
			if [ "$UID_HOME" != "$SCRIPT_DIR" ]; then
				echo "WARNING: Current user ${SCRIPT_UID} does NOT have access to ${UID_HOME} - fix that ASAP, as you'll run into permission problems soon enough!"
			fi

			# We can't do much more, avoid .NET failure at the very least
			UID_HOME="/tmp/asf-${SCRIPT_UID}"
			mkdir -p "$UID_HOME"
		fi

		export HOME="$UID_HOME"
		export XDG_RUNTIME_DIR="$UID_HOME"
	fi
fi

CONFIG_PATH="$(pwd)/${CONFIG_PATH}"

# Kill underlying ASF process on shell process exit
trap "trap - TERM && kill -- -$$" INT TERM

if ! command -v dotnet >/dev/null; then
	echo "ERROR: dotnet CLI tools are not installed!"
	exit 1
fi

dotnet --info

if [ "$SERVICE" -eq 1 ] || ([ -f "$CONFIG_PATH" ] && grep -Eq '"Headless":\s+?true' "$CONFIG_PATH"); then
	# We're running ASF in headless mode so we don't need STDIN
	# Start ASF in the background, trap will work properly due to non-blocking call
	if [ -n "$BINARY_PREFIX" ]; then
		$BINARY_PREFIX "dotnet ${DOTNET_ARGS-} $BINARY $*" &
	else
		dotnet ${DOTNET_ARGS-} "$BINARY" "$@" &
	fi

	# This will forward dotnet error code, set -e will abort the script if it's non-zero
	wait $!
else
	# We're running ASF in non-headless mode, so we need STDIN to be operative
	# Start ASF in the foreground, trap won't work until process exit
	if [ -n "$BINARY_PREFIX" ]; then
		$BINARY_PREFIX "dotnet ${DOTNET_ARGS-} $BINARY $*"
	else
		dotnet ${DOTNET_ARGS-} "$BINARY" "$@"
	fi
fi
