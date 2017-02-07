MONO_DEBUG_IF_AVAILABLE() {
	echo "INFO: Appending $1 to MONO_DEBUG..."

	local PREVIOUS_MONO_DEBUG="$MONO_DEBUG"

	# Add change if needed
	if [ -z "$PREVIOUS_MONO_DEBUG" ]; then
		export MONO_DEBUG="$1"
	elif echo "$PREVIOUS_MONO_DEBUG" | grep -Fq "$1"; then
		echo "DONE: $1 already exists"
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

	echo "DONE: $1"
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
	[  "$1" = "$(echo -e "$1\n$2" | sort -V | head -n 1)" ]
}

echo "INFO: Mono environment setup executed!"

MINIMUM_MONO_VERSION="4.6.0" # Bump as needed
CURRENT_MONO_VERSION="$(mono -V | head -n 1 | cut -d ' ' -f 5)"

echo "INFO: Mono version: $CURRENT_MONO_VERSION | Required: ${MINIMUM_MONO_VERSION}+"

if VERSION_LESS_THAN "$CURRENT_MONO_VERSION" "$MINIMUM_MONO_VERSION"; then
	echo "ERROR: You've attempted to build ASF with unsupported Mono version!"
	return 1
fi

MONO_DEBUG_IF_AVAILABLE "no-compact-seq-points"
MONO_DEBUG_IF_AVAILABLE "no-gdb-backtrace"

if [ -z "$MONO_ENV_OPTIONS" ]; then
	echo "INFO: Setting MONO_ENV_OPTIONS to: --server -O=all"
	export MONO_ENV_OPTIONS="--server -O=all"
else
	echo "INFO: Skipping setting of MONO_ENV_OPTIONS as it's already declared with value: $MONO_ENV_OPTIONS"
fi

MONO_FACADES=""
if [ -d "/usr/lib/mono/4.5/Facades" ]; then
	export MONO_FACADES="/usr/lib/mono/4.5/Facades"
else
	echo "WARN: Could not find Mono facades!"
fi

echo "INFO: Mono environment setup finished!"
