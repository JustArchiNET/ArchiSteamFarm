#!/bin/bash
set -eu

cd "$(dirname "$(readlink -f "$0")")"
cd ../..

crowdin -b master --identity tools/crowdin-cli/crowdin_identity.yml download
git reset

cd ASF-ui
git pull
git add -A "src/i18n/locale/*.json"
git commit -m "Translations update"
cd ..

cd ASF-WebConfigGenerator
git pull
git add -A "src/locale/*.json"
git commit -m "Translations update"
cd ..

cd wiki
git pull
git add -A "locale/*.md"
git commit -m "Translations update"
cd ..

git add -A "ArchiSteamFarm/Localization/*.resx" "ASF-ui" "ASF-WebConfigGenerator" "wiki"
git commit -m "Translations update"

git push --recurse-submodules=on-demand
read -p "Press enter to continue..."
