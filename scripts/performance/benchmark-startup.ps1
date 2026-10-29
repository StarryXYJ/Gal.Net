[CmdletBinding()]
param(
    [ValidateSet('Editor', 'Sample')][string]$Target = 'Editor',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateRange(1, 1000)][int]$Iterations = 5,
    [ValidateRange(0, 100)][int]$Warmup = 1,
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 60,
    [string]$GameDirectory,
    [string]$Profile,
    [string]$OutputDirectory,
    [string]$Executable,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$assemblyName = if ($Target -eq 'Editor') { 'GalNet.Editor' } else { 'GalNet.Sample.Avalonia' }
$group = if ($Target -eq 'Editor') { 'Editor' } else { 'Samples' }
$projectDirectory = Join-Path $repositoryRoot "src/$group/$assemblyName"
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot ('out/benchmarks/' + $Target + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
if ($Target -eq 'Sample') {
    if (-not $GameDirectory) { throw 'Sample requires -GameDirectory pointing to built game content. Build content separately.' }
    $GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
    if ($Profile) { $Profile = [IO.Path]::GetFullPath($Profile) }
}

$buildMilliseconds = $null
if (-not $SkipBuild) {
    $buildWatch = [Diagnostics.Stopwatch]::StartNew()
    $launchProperties = if ($Target -eq 'Sample') { @('-p:EnableLaunchArguments=true') } else { @() }
    & dotnet build (Join-Path $projectDirectory "$assemblyName.csproj") -c $Configuration -p:Benchmark=true @launchProperties -p:UseSharedCompilation=false -m:1 -nr:false -v:minimal *> (Join-Path $OutputDirectory 'build.log')
    $buildWatch.Stop()
    if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed. See $OutputDirectory/build.log" }
    $buildMilliseconds = $buildWatch.Elapsed.TotalMilliseconds
}
if ($Executable -and -not $SkipBuild) { throw '-Executable requires -SkipBuild; build and snapshot candidates before comparing.' }
$executable = if ($Executable) { [IO.Path]::GetFullPath($Executable) } else { Join-Path $projectDirectory "bin/benchmark/$Configuration/net10.0/$assemblyName.exe" }
if (-not (Test-Path -LiteralPath $executable)) { throw "Executable missing: $executable" }
$runs = [Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt ($Warmup + $Iterations); $index++) {
    $kind = if ($index -lt $Warmup) { 'warmup' } else { 'measured' }
    $runName = '{0}-{1:D3}' -f $kind, $index
    $tracePath = Join-Path $OutputDirectory "$runName.json"
    if (Test-Path -LiteralPath $tracePath) { throw "Trace already exists: $tracePath. Use a new output directory." }
    $startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
    $startInfo.WorkingDirectory = Split-Path -Parent $executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['GALNET_BENCHMARK_OUTPUT'] = $tracePath
    $startInfo.Environment['GALNET_BENCHMARK_EXIT'] = '1'
    if ($Target -eq 'Sample') {
        $startInfo.ArgumentList.Add($GameDirectory)
        if ($Profile) {
            $startInfo.ArgumentList.Add('--profile')
            $startInfo.ArgumentList.Add($Profile)
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $runWatch = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    try {
        $null = $process.Start()
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $runWatch.Stop()
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory "$runName.stdout.log")
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory "$runName.stderr.log")
        if ($timedOut) { throw "Startup timed out: $runName. Sample language selection requires an initialized profile." }
        if ($process.ExitCode -ne 0) { throw "Startup failed: $runName (exit $($process.ExitCode)). See run logs." }
    }
    finally {
        # Own only the child created by this iteration; never stop another Editor/game.
        if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if (-not (Test-Path -LiteralPath $tracePath)) { throw "No trace from $runName. Rebuild with -p:Benchmark=true." }
    $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
    if ($trace.droppedEvents -ne 0) { throw "Trace overflow: $runName" }
    if (-not @($trace.traceEvents | Where-Object name -eq 'Startup.Ready').Count) { throw "Startup did not reach ready: $runName" }
    $durations = @($trace.traceEvents | Where-Object ph -eq 'X')
    if (@($durations | Where-Object name -eq 'Startup.Total').Count -ne 1) { throw "Invalid startup total: $runName" }
    $runs.Add([pscustomobject]@{ Run = $runName; Warmup = ($kind -eq 'warmup'); ProcessLifetimeMs = $runWatch.Elapsed.TotalMilliseconds; Events = $durations })
    $total = ($durations | Where-Object name -eq 'Startup.Total').dur / 1000
    Write-Host ('{0}: Startup.Total = {1:F2} ms' -f $runName, $total)
}
$rows = foreach ($run in $runs) {
    if ($run.Warmup) { continue }
    foreach ($event in $run.Events) {
        [pscustomobject]@{ Run = $run.Run; Stage = $event.name; Milliseconds = $event.dur / 1000 }
    }
}
$summary = @($rows | Group-Object Stage | ForEach-Object {
    $values = @($_.Group.Milliseconds | Sort-Object)
    $count = $values.Count
    $median = if ($count % 2) { $values[[int][Math]::Floor($count / 2)] } else { ($values[$count / 2 - 1] + $values[$count / 2]) / 2 }
    [pscustomobject]@{
        Stage = $_.Name; Samples = $count
        MedianMs = $median
        P95Ms = $values[[Math]::Ceiling($count * 0.95) - 1]
        MinMs = $values[0]; MaxMs = $values[-1]
    }
} | Sort-Object MedianMs -Descending)
$rows | Export-Csv (Join-Path $OutputDirectory 'measurements.csv') -NoTypeInformation
$summary | Export-Csv (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation
$metadata = [ordered]@{
    Target = $Target; Configuration = $Configuration; Iterations = $Iterations; Warmup = $Warmup
    BuildMilliseconds = $buildMilliseconds; Utc = [DateTime]::UtcNow.ToString('O')
    Commit = (& git -C $repositoryRoot rev-parse HEAD); DirtyFiles = @(& git -C $repositoryRoot status --short)
    OS = [Environment]::OSVersion.ToString(); ProcessorCount = [Environment]::ProcessorCount
    Dotnet = (& dotnet --version); GameDirectory = $GameDirectory; Profile = $Profile
    Executable = $executable
    AssemblySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $executable) "$assemblyName.dll")).Hash
    Runs = @($runs | Select-Object Run, Warmup, ProcessLifetimeMs)
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'metadata.json')
$summary | Format-Table Stage, Samples, MedianMs, P95Ms -AutoSize
Write-Host "Results: $OutputDirectory"
