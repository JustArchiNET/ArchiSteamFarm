Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Push-Location "$PSScriptRoot\..\.."
& archi_core.ps1 --download
pause
