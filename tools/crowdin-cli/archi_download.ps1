Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Set-Location $PSScriptRoot
Set-Location ..\\..

crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml download
git reset

Push-Location -Path ASF-ui
git pull
git add -A "src\i18n\locale\*.json"
git commit -m "Translations update"
Pop-Location

Push-Location -Path ASF-WebConfigGenerator
git pull
git add -A "src\locale\*.json"
git commit -m "Translations update"
Pop-Location

Push-Location -Path wiki
git pull
git add -A "locale\*.md"
git commit -m "Translations update"
Pop-Location

git add -A "ArchiSteamFarm\Localization\*.resx" "ASF-ui" "ASF-WebConfigGenerator" "wiki"
git commit -m "Translations update"

git push --recurse-submodules=on-demand
pause
