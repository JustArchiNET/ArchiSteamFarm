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

	& "$archiCrowdinScriptPath" -t:$archiTargets -d -c -p # Download and commit all independent submodules that are part of ASF project
	& "$archiCrowdinScriptPath" -t:wiki -c -p # Wiki submodule depends on us, we do this one more time before in order to ensure that tree is up-to-date (e.g. branch is master before we start modifying files in the next step)
	& "$archiCrowdinScriptPath" -d -p -rs:no # Download translations for the main project, which also includes wiki submodule as of now
	& "$archiCrowdinScriptPath" -t:wiki -c -p # Commit the wiki submodule that we updated in the previous step
	& "$archiCrowdinScriptPath" -c # Commit the main project and references of all submodules
} finally {
	Pop-Location
}

pause
