#!/bin/bash
set -eu

SOURCE="_src" # Relative to script directory
OUTPUT="${SOURCE}/dist" # Relative to script directory

cd "$(dirname "$(readlink -f "$0")")"

npm install --prefix "$SOURCE"
npm run build --prefix "$SOURCE"

while read FILE; do
	rm -f "$FILE"
done < <(find . -mindepth 1 -maxdepth 1 -type l)

while read FILE; do
	ln -s "$FILE" .
done < <(find "$OUTPUT" -mindepth 1 -maxdepth 1)
