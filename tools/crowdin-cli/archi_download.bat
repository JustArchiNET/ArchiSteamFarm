@echo off
pushd %~dp0
cd ..\\..
call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yaml download
pause
