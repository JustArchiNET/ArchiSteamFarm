@echo off
pushd %~dp0
cd ..\\..
call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yaml upload sources
call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yaml download
git reset
git add -A "WebConfigGenerator\src\locale\*.json"
git commit -m "Translations update"
pause
