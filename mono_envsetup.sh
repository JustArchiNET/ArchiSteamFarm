# Constants
MINIMUM_MONO_VERSION="5.0.0" # This is Mono version required for both compilation + usage, bump as needed
MINIMUM_NET_FRAMEWORK="4.6.1" # This should be equal to <TargetFrameworkVersion> in ArchiSteamFarm.csproj, bump as needed

MONO_DEBUG_ADD_IF_AVAILABLE() {
	echo "INFO: Adding $1 to MONO_DEBUG..."

	local PREVIOUS_MONO_DEBUG="$MONO_DEBUG"

	# Add change if needed
	if [ -z "$PREVIOUS_MONO_DEBUG" ]; then
		export MONO_DEBUG="$1"
	elif echo "$PREVIOUS_MONO_DEBUG" | grep -Fq -- "$1"; then
		echo "INFO: $1 in MONO_DEBUG was set already"
		return 0
	else
		export MONO_DEBUG="${PREVIOUS_MONO_DEBUG},${1}"
	fi

	# If we did a change, check if Mono supports that option
	# If not, it will be listed as invalid option on line 1
	if mono "" 2>&1 | head -n 1 | grep -Fq "$1"; then
		echo "FAILED: $1"
		export MONO_DEBUG="$PREVIOUS_MONO_DEBUG"
		return 1
	fi

	echo "INFO: Added $1 to MONO_DEBUG"
	return 0
}

MONO_ENV_OPTIONS_ADD() {
	echo "INFO: Adding $1 to MONO_ENV_OPTIONS..."

	# Add change if needed
	if [ -z "$MONO_ENV_OPTIONS" ]; then
		export MONO_ENV_OPTIONS="$1"
	elif echo "$MONO_ENV_OPTIONS" | grep -Fq -- "$1"; then
		echo "INFO: $1 in MONO_ENV_OPTIONS was set already"
		return 0
	else
		export MONO_ENV_OPTIONS="${MONO_ENV_OPTIONS} ${1}"
	fi

	echo "INFO: Added $1 to MONO_ENV_OPTIONS"
	return 0
}

VERSION_GREATER_THAN() {
	if [ "$1" = "$2" ]; then
		return 1
	fi

	! VERSION_LESS_EQUAL_THAN "$1" "$2"
}

VERSION_GREATER_EQUAL_THAN() {
	! VERSION_LESS_THAN "$1" "$2"
}

VERSION_LESS_THAN() {
	if [ "$1" = "$2" ]; then
		return 1
	fi

	VERSION_LESS_EQUAL_THAN "$1" "$2"
}

VERSION_LESS_EQUAL_THAN() {
	[  "$1" = "$(echo -e "$1\n$2" | sort -t . -k 1,1n -k 2,2n -k 3,3n -k 4,4n | head -n 1)" ]
}

# Main
echo "INFO: Mono environment setup executed!"
CURRENT_MONO_VERSION="$(mono -V | head -n 1 | cut -d ' ' -f 5 | cut -d '.' -f '1-3')" # We take only first three version numbers, this is needed for facades path in OS X

echo "INFO: Mono version: $CURRENT_MONO_VERSION | Required: ${MINIMUM_MONO_VERSION}+"

if VERSION_LESS_THAN "$CURRENT_MONO_VERSION" "$MINIMUM_MONO_VERSION"; then
	echo "ERROR: You've attempted to build ASF with unsupported Mono version!"
	return 1
fi

MONO_DEBUG_ADD_IF_AVAILABLE "no-compact-seq-points"
MONO_DEBUG_ADD_IF_AVAILABLE "no-gdb-backtrace"

MONO_ENV_OPTIONS_ADD "-O=all"
MONO_ENV_OPTIONS_ADD "--server"

if [ -n "$MONO_FACADES" ]; then
	echo "INFO: Mono facades path was already set to: $MONO_FACADES"
else
	for MONO_LOCATION in "/opt/mono" "/usr" "/Library/Frameworks/Mono.framework/Versions/${CURRENT_MONO_VERSION}"; do
		for API in "${MINIMUM_NET_FRAMEWORK}-api" "4.5"; do # 4.5 is fallback path that existed before Mono decided to split Facades on per-API basis - still available
			if [ -d "${MONO_LOCATION}/lib/mono/${API}/Facades" ]; then
				export MONO_FACADES="${MONO_LOCATION}/lib/mono/${API}/Facades"
				break 2
			fi
		done
	done

	if [ -n "$MONO_FACADES" ]; then
		echo "INFO: Mono facades path resolved to: $MONO_FACADES"
	else
		echo "WARN: Could not find Mono facades!"
	fi
fi

echo "INFO: Mono environment setup finished!"
