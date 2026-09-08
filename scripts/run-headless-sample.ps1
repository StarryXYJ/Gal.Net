[CmdletBinding()]
param(
    [string]$Profile,
    [int]$LoadSlot = -1,
    [int]$SaveSlot = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $repositoryRoot 'GameTestCase'
$playerProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Headless\GalNet.Sample.Headless.csproj'

$Profile = if ($Profile) { [System.IO.Path]::GetFullPath($Profile) } else { Join-Path $repositoryRoot 'artifacts\sample-profiles\headless' }
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
