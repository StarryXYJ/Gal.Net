[CmdletBinding()]
param(
    [string]$Project,
    [string]$BuildOutput,
    [switch]$SkipBuild,
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$Project = if ($Project) { [System.IO.Path]::GetFullPath($Project) } else { Join-Path $repositoryRoot 'GameTestCase' }
$gameDirectory = if ($BuildOutput) { [System.IO.Path]::GetFullPath($BuildOutput) } else { Join-Path $Project 'Output' }
$sampleProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Avalonia\GalNet.Sample.Avalonia.csproj'
$editorProject = Join-Path $repositoryRoot 'src\Editor\GalNet.Editor.Headless\GalNet.Editor.Headless.csproj'

if (-not $SkipBuild)
{
    & dotnet run --project $editorProject -- build $Project --output $gameDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$sampleArguments = @($gameDirectory)
if ($Profile)
{
    $sampleArguments += '--profile', [System.IO.Path]::GetFullPath($Profile)
}

Write-Host "Launching Avalonia sample: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $sampleProject -- @sampleArguments
exit $LASTEXITCODE
