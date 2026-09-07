[CmdletBinding()]
param(
    [switch]$KeepProfile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $repositoryRoot 'samples\HeadlessPersistenceSimulation'
$playerProject = Join-Path $repositoryRoot 'src\Samples\GalNet.Sample.Headless\GalNet.Sample.Headless.csproj'
$profileDirectory = Join-Path $repositoryRoot 'artifacts\headless-persistence-simulation'

function Invoke-HeadlessPlayer {
    param([string[]]$PlayerArguments)

    # The fixture has one dialogue line.  Pipe one empty confirmation so the run is unattended.
    $output = '' | & dotnet run --project $playerProject -- @PlayerArguments 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "Headless player failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }

    return $output
}

try
{
    if (Test-Path -LiteralPath $profileDirectory)
    {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force
    }

    $firstRun = Invoke-HeadlessPlayer @($gameDirectory, '--profile', $profileDirectory, '--save-slot', '0')
    $firstRunText = $firstRun -join [Environment]::NewLine
    if ($firstRunText -notmatch '\[Save\] wrote slot 0')
    {
        throw 'The initial run did not write save slot 0.'
    }

    $secondRun = Invoke-HeadlessPlayer @($gameDirectory, '--profile', $profileDirectory, '--load-slot', '0', '--save-slot', '1')
    $secondRunText = $secondRun -join [Environment]::NewLine
    if ($secondRunText -notmatch '\[Save\] loaded slot 0')
    {
        throw 'The second run did not load save slot 0.'
    }

    $slotZeroPath = Join-Path $profileDirectory 'saves\slot-000.json'
    $slotOnePath = Join-Path $profileDirectory 'saves\slot-001.json'
    $playerVariablesPath = Join-Path $profileDirectory 'player-variables.json'
    if (!(Test-Path -LiteralPath $slotZeroPath) -or !(Test-Path -LiteralPath $slotOnePath) -or !(Test-Path -LiteralPath $playerVariablesPath))
    {
        throw 'Expected save slots or player-variable file were not created.'
    }

    $playerVariables = Get-Content -LiteralPath $playerVariablesPath -Raw | ConvertFrom-Json
    if ($playerVariables.simulation_completed.Value -ne $true)
    {
        throw 'The global player variable was not persisted.'
    }

    $slotZero = Get-Content -LiteralPath $slotZeroPath -Raw | ConvertFrom-Json
    if ($slotZero.Snapshot.Variables.simulation_slot_marker.Value -ne 7)
    {
        throw 'The save-scoped variable was not persisted in slot 0.'
    }

    Write-Host 'Headless persistence simulation passed: save/load and global player variables were verified.' -ForegroundColor Green
    if ($KeepProfile)
    {
        Write-Host "Profile retained at: $profileDirectory"
    }
}
finally
{
    if (!$KeepProfile -and (Test-Path -LiteralPath $profileDirectory))
    {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force
    }
}
