@echo off
pushd %~dp0
cd ..\\..

cd wiki
git reset --hard
git clean -fd
git pull
cd ..

call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml upload sources
pause
