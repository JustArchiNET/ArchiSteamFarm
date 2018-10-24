Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Set-Location $PSScriptRoot
Set-Location ..\\..

Push-Location -Path ASF-ui
git reset --hard
git clean -fd
git pull
Pop-Location

Push-Location -Path ASF-WebConfigGenerator
git reset --hard
git clean -fd
git pull
Pop-Location

Push-Location -Path wiki
git reset --hard
git clean -fd
git pull
Pop-Location

crowdin -b master --identity tools\\crowdin-cli\\crowdin_identity.yml upload sources
pause
