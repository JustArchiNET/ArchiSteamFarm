Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Set-Location $PSScriptRoot
Set-Location ..\\..

Push-Location -Path wiki
git reset --hard
git clean -fd
git pull
Pop-Location

crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml upload sources

crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml download
git reset

Push-Location -Path wiki
git pull
git add -A "locale\*.md"
git commit -m "Translations update"
Pop-Location

git add -A "ArchiSteamFarm\Localization\*.resx" "ArchiSteamFarm\www\locale\*.json" "WebConfigGenerator\src\locale\*.json" "wiki"
git commit -m "Translations update"

git push --recurse-submodules=on-demand
pause
