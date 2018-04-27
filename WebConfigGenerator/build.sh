#!/bin/bash
set -eu

OUTPUT="dist"

cd "$(dirname "$(readlink -f "$0")")"

git pull

npm install
npm run build

git reset
git add -A -f "$OUTPUT"
git commit -m "WebConfigGenerator build"
git push
