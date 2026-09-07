[CmdletBinding()]
param(
    [string]$Profile,
    [int]$LoadSlot = -1,
    [int]$SaveSlot = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $repositoryRoot 'samples\HeadlessPersistenceSimulation'
$playerProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Headless\GalNet.Sample.Headless.csproj'

$playerArguments = @($gameDirectory)
if ($Profile)
{
    $playerArguments += '--profile', $Profile
}
if ($LoadSlot -ge 0)
{
    $playerArguments += '--load-slot', $LoadSlot.ToString()
}
if ($SaveSlot -ge 0)
{
    $playerArguments += '--save-slot', $SaveSlot.ToString()
}

Write-Host "Launching sample game: $gameDirectory" -ForegroundColor Cyan
& dotnet run --project $playerProject -- @playerArguments
exit $LASTEXITCODE
