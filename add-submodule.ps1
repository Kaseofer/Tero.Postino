param(
	[Parameter(Mandatory=$true)]
	[string]$RepoUrl,

	[Parameter(Mandatory=$true)]
	[string]$AppHostPath,

	[switch]$Force,

	[switch]$Push
)

function Fail([string]$msg) { Write-Error $msg; exit 1 }

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail "git not found in PATH." }

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$localScaffold = Join-Path $scriptRoot '..\..\scaffold\Tero.Postino' | Resolve-Path -ErrorAction SilentlyContinue

Write-Host "Add Postino submodule helper"
Write-Host "RepoUrl: $RepoUrl"
Write-Host "AppHostPath: $AppHostPath"

if ($localScaffold) {
	Write-Host "Local scaffold found at: $localScaffold"
	if (-not $Force) {
		$ok = Read-Host "Remove local scaffold folder and continue? (y/n)"
		if ($ok -ne 'y') { Write-Host 'Aborting.'; exit 0 }
	}

	Write-Host "Removing local scaffold folder..."
	git rm -r --cached -q "$scriptRoot\..\..\scaffold\Tero.Postino" 2>$null
	Remove-Item -Recurse -Force "$scriptRoot\..\..\scaffold\Tero.Postino"
	git commit -m "Remove local Postino scaffold (moved to independent repo)" 2>$null || Write-Host "No commit created (maybe nothing staged)."
}

if (-not (Test-Path $AppHostPath)) { Fail "AppHostPath '$AppHostPath' does not exist." }

Push-Location $AppHostPath
try {
	Write-Host "Adding submodule to $AppHostPath at services/postino ..."
	git submodule add $RepoUrl services/postino
	git add .gitmodules services/postino
	git commit -m "Add Postino submodule" || Write-Host "No commit created (maybe already added)."

	Write-Host "Adding Postino projects to solution if present..."
	$projPaths = @(
		"services/postino/src/Tero.Postino.Api/Tero.Postino.Api.csproj",
		"services/postino/src/Tero.Postino.Application/Tero.Postino.Application.csproj",
		"services/postino/src/Tero.Postino.Infrastructure/Tero.Postino.Infrastructure.csproj",
		"services/postino/src/Tero.Postino.Domain/Tero.Postino.Domain.csproj"
	)
	foreach ($p in $projPaths) {
		if (Test-Path $p) {
			Write-Host "Adding $p to solution..."
			dotnet sln add $p
		} else {
			Write-Host "Project not found (skipping): $p"
		}
	}

	# Stage and commit solution changes (if any) and push the apphost repo
	git add .
	git commit -m "Add Postino projects to solution and register submodule" 2>$null || Write-Host "No new changes to commit in apphost."
	if ($Push) {
		try {
			Write-Host "Pushing commits to remote..."
			git push -u origin HEAD
		}
		catch {
			Write-Warning "Push failed: $_.Exception.Message"
		}
	}
	else {
		Write-Host "Skipping push to remote. Review changes locally and push when ready."
	}

	Write-Host "Submodule added and apphost updated."
}
finally {
	Pop-Location
}
