#!/usr/bin/env sh
set -eu

CONFIG_PATH="config/ASF.json"

cd "$(dirname "$(readlink -f "$0")")"

SCRIPT_DIR="$(pwd)"
SCRIPT_PATH="${SCRIPT_DIR}/${0}"

BINARY="${SCRIPT_DIR}/ArchiSteamFarm"

if [ ! -f "$BINARY" ]; then
	echo "ERROR: $BINARY could not be found!"
	exit 1
fi

BINARY_ARGS=""
PATH_NEXT=0

PARSE_ARG() {
	BINARY_ARGS="$BINARY_ARGS $1"

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

CONFIG_PATH="$(pwd)/${CONFIG_PATH}"

# Kill underlying ASF process on shell process exit
trap "trap - TERM && kill -- -$$" INT TERM

# TODO: Workaround for https://github.com/JustArchiNET/ArchiSteamFarm/issues/1812
if [ -n "${DOTNET_RUNNING_IN_CONTAINER-}" ] && [ "$DOTNET_RUNNING_IN_CONTAINER" = "true" ]; then
	(
		loops=6 # Maximum of 60 seconds for unpack

		while [ "$loops" -gt 0 ]; do
			sleep 10

			if [ -d "/var/tmp/.net" ]; then
				find "/var/tmp/.net" -mindepth 2 -name '*\\*' | while IFS="" read -r broken_path; do
					fixed_path="$(echo "$broken_path" | sed 's/\\/\//g')"

					mkdir -p "$(dirname "$fixed_path")"
					mv "$broken_path" "$fixed_path"
				done
			fi

			loops="$((loops-1))"
		done
	) &
fi

while :; do
	if [ -f "$CONFIG_PATH" ] && grep -Eq '"Headless":\s+?true' "$CONFIG_PATH"; then
		# We're running ASF in headless mode so we don't need STDIN
		"$BINARY" $BINARY_ARGS & # Start ASF in the background, trap will work properly due to non-blocking call
		wait $! # This will forward dotnet error code, set -e will abort the script if it's non-zero
	else
		# We're running ASF in non-headless mode, so we need STDIN to be operative
		"$BINARY" $BINARY_ARGS # Start ASF in the foreground, trap sadly won't work until process exit
	fi

	chmod +x "$SCRIPT_PATH" # If ASF exited by itself, we need to ensure that our script is still set to +x after auto-update
	sleep 1
done
