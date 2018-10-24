#!/bin/bash
set -eu

cd "$(dirname "$(readlink -f "$0")")"
cd ../..

cd ASF-ui
git reset --hard
git clean -fd
git pull
cd ..

cd ASF-WebConfigGenerator
git reset --hard
git clean -fd
git pull
cd ..

cd wiki
git reset --hard
git clean -fd
git pull
cd ..

crowdin -b master --identity tools/crowdin-cli/crowdin_identity.yml upload sources
read -p "Press enter to continue..."
