#!/bin/bash

MONO_DEBUG_IF_AVAILABLE() {
	local PREVIOUS_MONO_DEBUG="$MONO_DEBUG"

	if [[ -z "$PREVIOUS_MONO_DEBUG" ]]; then
		export MONO_DEBUG="$1"
	elif echo "$PREVIOUS_MONO_DEBUG" | grep -Fq "$1"; then
		return 0
	else
		export MONO_DEBUG="${PREVIOUS_MONO_DEBUG},${1}"
	fi

	if mono "" 2>&1 | head -n 1 | grep -Fq "$1"; then
		export MONO_DEBUG="$PREVIOUS_MONO_DEBUG"
		return 1
	fi

	return 0
}

# https://bugzilla.xamarin.com/show_bug.cgi?id=42606
MONO_DEBUG_IF_AVAILABLE "no-compact-seq-points"
