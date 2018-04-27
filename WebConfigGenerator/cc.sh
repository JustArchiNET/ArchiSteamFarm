#!/bin/bash
set -eu

OUTPUT="dist" # Relative to script directory
TARGET="../docs" # Relative to script directory
SRC="WebConfigGenerator" # Path between $OUTPUT and $TARGET

cd "$(dirname "$(readlink -f "$0")")"

cd "$SRC_DIR"

git pull

npm install
npm run build

while read -r FILE; do
	rm -f "$FILE"
done < <(find "$TARGET" -mindepth 1 -maxdepth 1 -type l)

while read -r FILE; do
	ln -s "${SRC}/${FILE}" "$TARGET"
done < <(find "$OUTPUT" -mindepth 1 -maxdepth 1)

git reset
git add -A -f "$OUTPUT" "$TARGET"
git commit -m "WebConfigGenerator build"
git push
