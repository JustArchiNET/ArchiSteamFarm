#!/bin/bash
set -eu

cd "$(dirname "$(readlink -f "$0")")"
cd ../..

cd wiki
git reset --hard
git clean -fd
git pull
cd ..

crowdin -b master --identity tools/crowdin-cli/crowdin_identity.yml upload sources

crowdin -b master --identity tools/crowdin-cli/crowdin_identity.yml download
git reset

cd wiki
git pull
git add -A "locale/*.md"
git commit -m "Translations update"
cd ..

git add -A "ArchiSteamFarm/Localization/*.resx" "ArchiSteamFarm/www/locale/*.json" "WebConfigGenerator/src/locale/*.json" "wiki"
git commit -m "Translations update"

git push --recurse-submodules=on-demand
read -p "Press enter to continue..."
