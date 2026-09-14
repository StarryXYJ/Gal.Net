[CmdletBinding()]
param(
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $repositoryRoot 'GameTestCase'
$sampleProject = Join-Path $repositoryRoot 'src\GalNet.Sample.Avalonia\GalNet.Sample.Avalonia.csproj'
$sampleArguments = @($gameDirectory)
if ($Profile)
{
    $sampleArguments += '--profile', [System.IO.Path]::GetFullPath($Profile)
}

Write-Host "Launching Avalonia sample: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $sampleProject -- @sampleArguments
exit $LASTEXITCODE
