[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Editor', 'Sample')][string]$Target = 'Editor',
    [string]$GameDirectory,
    [string]$Profile,
    [ValidateRange(3, 100)][int]$Pairs = 10
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$executables = @{ A = [IO.Path]::GetFullPath($Baseline); B = [IO.Path]::GetFullPath($Candidate) }
$benchmark = Join-Path $PSScriptRoot 'benchmark-startup.ps1'
foreach ($label in @('A', 'B')) {
    & $benchmark -Target $Target -GameDirectory $GameDirectory -Profile $Profile -SkipBuild -Executable $executables[$label] -Warmup 0 -Iterations 1 -OutputDirectory (Join-Path $OutputDirectory "warmup-$label") *> (Join-Path $OutputDirectory "warmup-$label.log")
}
$results = [Collections.Generic.List[object]]::new()
for ($pair = 0; $pair -lt $Pairs; $pair++) {
    # AB, BA alternation balances order effects; both candidates use the same ready milestone.
    $order = if ($pair % 2) { @('B', 'A') } else { @('A', 'B') }
    foreach ($label in $order) {
        $runDirectory = Join-Path $OutputDirectory ('pair-{0:D2}-{1}' -f $pair, $label)
        & $benchmark -Target $Target -GameDirectory $GameDirectory -Profile $Profile -SkipBuild -Executable $executables[$label] -Warmup 0 -Iterations 1 -OutputDirectory $runDirectory *> "$runDirectory.log"
        $trace = Get-Content (Join-Path $runDirectory 'measured-000.json') -Raw | ConvertFrom-Json
        $total = ($trace.traceEvents | Where-Object name -eq 'Startup.Total').dur / 1000
        $results.Add([pscustomobject]@{ Pair = $pair; Label = $label; Milliseconds = $total })
    }
    Write-Host ('Pair {0}/{1} complete' -f ($pair + 1), $Pairs)
}
$results | Export-Csv (Join-Path $OutputDirectory 'comparison.csv') -NoTypeInformation
$statistics = foreach ($label in @('A', 'B')) {
    $values = @($results | Where-Object Label -eq $label | Select-Object -ExpandProperty Milliseconds | Sort-Object)
    $count = $values.Count
    $median = if ($count % 2) { $values[[int][Math]::Floor($count / 2)] } else { ($values[$count / 2 - 1] + $values[$count / 2]) / 2 }
    [pscustomobject]@{ Label = $label; Samples = $count; MedianMs = $median; P95Ms = $values[[Math]::Ceiling($count * 0.95) - 1] }
}
$statistics | Export-Csv (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation
$statistics | Format-Table -AutoSize
