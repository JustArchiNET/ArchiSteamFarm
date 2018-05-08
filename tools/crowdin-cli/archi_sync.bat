@echo off
pushd %~dp0
cd ..\\..
call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml upload sources

call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml download
git reset

cd wiki
git reset
git add -A "locale\*.md"
git commit -m "Translations update"
cd ..

git add -A "ArchiSteamFarm\Localization\*.resx" "ArchiSteamFarm\www\locale\*.json" "WebConfigGenerator\src\locale\*.json" "wiki"
git commit -m "Translations update"

git push --recurse-submodules=on-demand
pause
