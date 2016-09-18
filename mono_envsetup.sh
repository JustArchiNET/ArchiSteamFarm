MONO_DEBUG_IF_AVAILABLE() {
	local PREVIOUS_MONO_DEBUG="$MONO_DEBUG"

	# Add change if needed
	if [ -z "$PREVIOUS_MONO_DEBUG" ]; then
		export MONO_DEBUG="$1"
	elif echo "$PREVIOUS_MONO_DEBUG" | grep -Fq "$1"; then
		echo "Success: $1 already exists"
		return 0
	else
		export MONO_DEBUG="${PREVIOUS_MONO_DEBUG},${1}"
	fi

	# If we did a change, check if Mono supports that option
	# If not, it will be listed as invalid option on line 1
	if mono "" 2>&1 | head -n 1 | grep -Fq "$1"; then
		echo "Failure: $1"
		export MONO_DEBUG="$PREVIOUS_MONO_DEBUG"
		return 1
	fi

	echo "Success: $1"
	return 0
}

VERSION_GREATER() {
	if [ "$1" = "$2" ]; then
		return 1
	fi

	! VERSION_LESS_EQUAL "$1" "$2"
}

VERSION_GREATER_EQUAL() {
	! VERSION_LESS "$1" "$2"
}

VERSION_LESS() {
	if [ "$1" = "$2" ]; then
		return 1
	fi

	VERSION_LESS_EQUAL "$1" "$2"
}

VERSION_LESS_EQUAL() {
	[  "$1" = "$(echo -e "$1\n$2" | sort -V | head -n 1)" ]
}

echo "Mono environment setup executed!"

MONO_VERSION="$(mono -V | head -n 1 | cut -d ' ' -f 5)"

echo "Mono version: $MONO_VERSION"

if VERSION_GREATER_EQUAL "$MONO_VERSION" "4.6.0"; then
	echo "INFO: Appending no-compact-seq-points to MONO_DEBUG..."
	MONO_DEBUG_IF_AVAILABLE "no-compact-seq-points"
fi

if [ -z "$MONO_ENV_OPTIONS" ]; then
	echo "INFO: Setting MONO_ENV_OPTIONS to: --server -O=all"
	export MONO_ENV_OPTIONS="--server -O=all"
else
	echo "INFO: Skipping setting of MONO_ENV_OPTIONS as it's already declared with value: $MONO_ENV_OPTIONS"
fi

echo "Mono environment setup finished!"
