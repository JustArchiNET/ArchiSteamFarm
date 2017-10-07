#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

if [[ -z "${ASF_ARGS-}" ]]; then
	ASF_ARGS=""
fi

trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT

while [[ -f ArchiSteamFarm.dll ]]; do
	dotnet ArchiSteamFarm.dll --service $ASF_ARGS # We will abort the script if ASF exits with an error
	sleep 1
done
