#!/bin/bash
set -eu

TARGET="../docs" # Relative to script directory

cd "$(dirname "$(readlink -f "$0")")"

git pull

npm install
npm run build

git reset
git add -A -f "$TARGET"
git commit -m "WebConfigGenerator build"
git push
