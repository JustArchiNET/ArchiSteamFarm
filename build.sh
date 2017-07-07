#!/bin/bash
set -eu

SOURCE="_src"
OUTPUT="${SOURCE}/dist"

cd "$(dirname "$(readlink -f "$0")")"

cd "$SOURCE"
npm install
npm run build
cd ..

while read FILE; do
	rm -f "$FILE"
done < <(find . -mindepth 1 -maxdepth 1 -type l)

while read FILE; do
	ln -s "$FILE" .
done < <(find "$OUTPUT" -mindepth 1 -maxdepth 1)
