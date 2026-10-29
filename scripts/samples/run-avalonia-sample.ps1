[CmdletBinding()]
param(
    [string]$Project,
    [string]$BuildOutput,
    [switch]$SkipBuild,
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$defaultProject = Join-Path $repositoryRoot 'GameTestCase'
$Project = if ($Project) { [System.IO.Path]::GetFullPath($Project) } else { $defaultProject }
$gameDirectory = if ($BuildOutput) { [System.IO.Path]::GetFullPath($BuildOutput) } else { Join-Path $Project 'Output' }
$profileDirectory = if ($Profile) { [System.IO.Path]::GetFullPath($Profile) } else { Join-Path $Project '.galnet' }
$sampleProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Avalonia\GalNet.Sample.Avalonia.csproj'
$editorProject = Join-Path $repositoryRoot 'src\Editor\GalNet.Editor.Headless\GalNet.Editor.Headless.csproj'

if (-not $SkipBuild)
{
    if ([string]::Equals([System.IO.Path]::TrimEndingDirectorySeparator($Project), $defaultProject, [StringComparison]::OrdinalIgnoreCase))
    {
        $contractsProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Contracts\GalNet.Sample.Contracts.csproj'
        & (Join-Path $repositoryRoot 'scripts\contracts\build-project-contracts.ps1') -ContractsProject $contractsProject -ProjectDirectory $Project
    }
    & dotnet run --project $editorProject -- build $Project --output $gameDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$sampleArguments = @($gameDirectory, '--profile', $profileDirectory)

Write-Host "Launching Avalonia sample: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $sampleProject -- @sampleArguments
exit $LASTEXITCODE
