[CmdletBinding()]
param(
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $repositoryRoot 'GameTestCase'
$sampleProject = Join-Path $repositoryRoot 'src\GalNet.Sample.Avalonia\GalNet.Sample.Avalonia.csproj'
$Profile = if ($Profile) { [System.IO.Path]::GetFullPath($Profile) } else { Join-Path $repositoryRoot 'artifacts\sample-profiles\avalonia' }

Write-Host "Launching Avalonia sample: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $sampleProject -- $gameDirectory --profile $Profile
exit $LASTEXITCODE
