Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$archiCrowdinPath = 'tools\ArchiCrowdin'
$archiCrowdinScriptPath = "$archiCrowdinPath\archi.ps1"

$crowdinConfigFileName = 'crowdin.yml'
$crowdinIdentityFileName = 'crowdin_identity.yml'
$crowdinIdentityPath = "$archiCrowdinPath\$crowdinIdentityFileName"

$archiTargets = @('ASF-ui', 'ASF-WebConfigGenerator')

Push-Location "$PSScriptRoot"

try {
	for ($i = 0; ($i -lt 3) -and (!(Test-Path "$crowdinConfigFileName" -PathType Leaf)); $i++) {
		Set-Location ..
	}

	if (!(Test-Path "$crowdinConfigFileName" -PathType Leaf)) {
		throw "$crowdinConfigFileName could not be found, aborting."
	}

	if (!(Test-Path "$archiCrowdinScriptPath" -PathType Leaf)) {
		throw "$archiCrowdinScriptPath could not be found, aborting."
	}

	if (!(Test-Path "$crowdinIdentityPath" -PathType Leaf)) {
		throw "$crowdinIdentityPath could not be found, aborting."
	}

	& "$archiCrowdinScriptPath" -t:$archiTargets -d -c -p
	& "$archiCrowdinScriptPath" -d -p
	& "$archiCrowdinScriptPath" -t:wiki -c -p
	& "$archiCrowdinScriptPath" -c -p
} finally {
	Pop-Location
}

pause
