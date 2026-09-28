[CmdletBinding()]
param(
    [string]$Project,
    [string]$BuildOutput,
    [switch]$SkipBuild,
    [string]$Profile,
    [int]$LoadSlot = -1,
    [int]$SaveSlot = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$Project = if ($Project) { [System.IO.Path]::GetFullPath($Project) } else { Join-Path $repositoryRoot 'GameTestCase' }
$gameDirectory = if ($BuildOutput) { [System.IO.Path]::GetFullPath($BuildOutput) } else { Join-Path $Project 'Output' }
$playerProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Headless\GalNet.Sample.Headless.csproj'
$editorProject = Join-Path $repositoryRoot 'src\GalNet.Editor.Headless\GalNet.Editor.Headless.csproj'

if (-not $SkipBuild)
{
    & dotnet run --project $editorProject -- build $Project --output $gameDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$Profile = if ($Profile) { [System.IO.Path]::GetFullPath($Profile) } else { Join-Path $Project '.galnet\headless' }
$playerArguments = @($gameDirectory)
$playerArguments += '--profile', $Profile
if ($LoadSlot -ge 0)
{
    $playerArguments += '--load-slot', $LoadSlot.ToString()
}
if ($SaveSlot -ge 0)
{
    $playerArguments += '--save-slot', $SaveSlot.ToString()
}

Write-Host "Launching Headless sample: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $playerProject -- @playerArguments
exit $LASTEXITCODE
