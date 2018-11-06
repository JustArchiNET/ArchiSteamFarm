Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$branch = 'master'
$crowdinConfigPath = 'crowdin.yml'
$crowdinHomePath = 'tools\crowdin-cli'
$crowdinIdentityPath = "$crowdinHomePath\crowdin_identity.yml"
$crowdinJarPath = "$crowdinHomePath\crowdin-cli.jar"
$projectHomePath = '..\..'

function Commit-Module($project, $path) {
	Push-Location "$project"

	try {
		git pull origin "$branch"

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}

		git add -A "$path"

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}

		git diff-index --quiet HEAD

		if ($LastExitCode -ne 0) {
			git commit -m "Translations update"

			if ($LastExitCode -ne 0) {
				throw "Last command failed."
			}
		}
	} finally {
		Pop-Location
	}
}

function Crowdin-Download {
	Pull-Module 'ASF-ui'
	Pull-Module 'ASF-WebConfigGenerator'
	Pull-Module 'wiki'

	Crowdin-Execute 'download'

	Commit-Module 'ASF-ui' 'src\i18n\locale\*.json'
	Commit-Module 'ASF-WebConfigGenerator' 'src\locale\*.json'
	Commit-Module 'wiki' 'locale\*.md'

	git reset

	if ($LastExitCode -ne 0) {
		throw "Last command failed."
	}

	git add -A "ArchiSteamFarm\Localization\*.resx" "ASF-ui" "ASF-WebConfigGenerator" "wiki"

	if ($LastExitCode -ne 0) {
		throw "Last command failed."
	}

	git diff-index --quiet HEAD

	if ($LastExitCode -ne 0) {
		git commit -m "Translations update"

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}
	}

	git push origin "$branch" --recurse-submodules=on-demand

	if ($LastExitCode -ne 0) {
		throw "Last command failed."
	}
}

function Crowdin-Execute($command) {
	if (Get-Command 'crowdin' -ErrorAction SilentlyContinue) {
		& crowdin -b "$branch" --identity "$crowdinIdentityPath" $command

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}
	} elseif ((Test-Path "$crowdinJarPath" -PathType Leaf) -and (Get-Command 'java' -ErrorAction SilentlyContinue)) {
		& java -jar "$crowdinJarPath" -b "$branch" --identity "$crowdinIdentityPath" $command

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}
	} else {
		throw "Could not find crowdin executable!"
	}
}

function Crowdin-Upload {
	Pull-Module 'ASF-ui'
	Pull-Module 'ASF-WebConfigGenerator'
	Pull-Module 'wiki'

	Crowdin-Execute 'upload sources'
}

function Pull-Module($project) {
	Push-Location "$project"

	try {
		git checkout -f "$branch"

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}

		git reset --hard

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}

		git clean -fd

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}

		git pull origin "$branch"

		if ($LastExitCode -ne 0) {
			throw "Last command failed."
		}
	} finally {
		Pop-Location
	}
}

Push-Location "$PSScriptRoot\$projectHomePath"

try {
	if (!(Test-Path "$crowdinConfigPath" -PathType Leaf)) {
		throw "$crowdinConfigPath could not be found, aborting."
	}

	if (!(Test-Path "$crowdinIdentityPath" -PathType Leaf)) {
		throw "$crowdinIdentityPath could not be found, aborting."
	}

	foreach ($arg in $args) {
		switch -Wildcard ($arg) {
			'*download' {
				Crowdin-Download
			}
			'*upload' {
				Crowdin-Upload
			}
			default {
				throw "$arg action is unknown, aborting."
			}
		}
	}
} finally {
	Pop-Location
}
