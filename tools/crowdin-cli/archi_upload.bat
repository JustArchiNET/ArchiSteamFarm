@echo off
cd ..\\..
call crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yaml upload sources
pause
