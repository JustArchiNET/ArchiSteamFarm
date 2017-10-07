#!/bin/bash
set -eu
cd "$(dirname "$(readlink -f "$0")")"

trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT

while [[ -f ArchiSteamFarm.dll ]]; do
	dotnet ArchiSteamFarm.dll --service # We will abort the script if ASF exits with an error
	sleep 1
done
