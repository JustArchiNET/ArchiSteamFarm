#!/usr/bin/env sh
set -eu

CONFIG_PATH="config/ASF.json"
OS_TYPE="$(uname -s)"

case "$OS_TYPE" in
	"Darwin") SCRIPT_PATH="$(readlink "$0")" ;;
	"FreeBSD") SCRIPT_PATH="$(readlink -f "$0")" ;;
	"Linux") SCRIPT_PATH="$(readlink -f "$0")" ;;
	*) echo "ERROR: Unknown OS type: ${OS_TYPE}. If you believe that our script should work on your machine, please let us know."; exit 1
esac

SCRIPT_DIR="$(dirname "$SCRIPT_PATH")"
BINARY="${SCRIPT_DIR}/ArchiSteamFarm"

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

if [ -n "${ASF_USER-}" ] && [ "$(id -u)" -eq 0 ] && id -u "$ASF_USER" >/dev/null 2>&1 && [ "$(id -u "$ASF_USER")" -gt 0 ]; then
	# Fix permissions first to ensure ASF has read/write access to the directory specified by --path and its own
	chown -hR "${ASF_USER}:${ASF_USER}" . "$SCRIPT_DIR" || true

	BINARY_PREFIX="su ${ASF_USER} -c"
fi

CONFIG_PATH="$(pwd)/${CONFIG_PATH}"

# Kill underlying ASF process on shell process exit
trap "trap - TERM && kill -- -$$" INT TERM

while :; do
	if [ "$SERVICE" -eq 1 ] || ([ -f "$CONFIG_PATH" ] && grep -Eq '"Headless":\s+?true' "$CONFIG_PATH"); then
		# We're running ASF in headless mode so we don't need STDIN
		# Start ASF in the background, trap will work properly due to non-blocking call
		if [ -n "$BINARY_PREFIX" ]; then
			$BINARY_PREFIX "$BINARY $*" &
		else
			"$BINARY" "$@" &
		fi

		# This will forward dotnet error code, set -e will abort the script if it's non-zero
		wait $!
	else
		# We're running ASF in non-headless mode, so we need STDIN to be operative
		# Start ASF in the foreground, trap won't work until process exit
		if [ -n "$BINARY_PREFIX" ]; then
			$BINARY_PREFIX "$BINARY $*"
		else
			"$BINARY" "$@"
		fi
	fi

	sleep 1
done
