[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory,
    [string[]]$ApplicationArguments = @(),
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot ('artifacts/benchmarks/native-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -ErrorAction Stop
$wpr = (Get-Command wpr.exe -ErrorAction Stop).Source
$state = & $wpr -status 2>&1 | Out-String
$state | Set-Content (Join-Path $OutputDirectory 'wpr-before.log')
# Fail closed for an existing recorder or an unrecognized/localized status.
if ($state -notmatch 'WPR is not recording') { throw 'WPR is not confirmed idle; existing recordings are never stopped.' }
$trace = Join-Path $OutputDirectory 'startup.etl'
$cpuPath = Join-Path $OutputDirectory 'startup.cpu.json'
$profile = (Join-Path $PSScriptRoot 'startup-native.wprp') + '!StartupCPU'
$process = [Diagnostics.Process]::new()
$startInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
$startInfo.WorkingDirectory = Split-Path -Parent $Executable
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.Environment['GALNET_BENCHMARK_OUTPUT'] = $cpuPath
$startInfo.Environment['GALNET_BENCHMARK_EXIT'] = '1'
$startInfo.Environment['GALNET_BENCHMARK_EVENTPIPE'] = '1'
foreach ($argument in $ApplicationArguments) { $startInfo.ArgumentList.Add($argument) }
$process.StartInfo = $startInfo
$recording = $false
$started = $false
try {
    & $wpr -start $profile -filemode *> (Join-Path $OutputDirectory 'wpr-start.log')
    if ($LASTEXITCODE -ne 0) { throw 'WPR start failed; see wpr-start.log. An elevated terminal is required.' }
    $recording = $true
    $null = $process.Start()
    $started = $true
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'stdout.log')
    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'stderr.log')
    [ordered]@{
        Executable = $Executable
        ProcessId = $process.Id
        ExitCode = $process.ExitCode
        AssemblySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath ([IO.Path]::ChangeExtension($Executable, '.dll'))).Hash
        Utc = [DateTime]::UtcNow.ToString('O')
        DiagnosticOnly = $true
    } | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'metadata.json')
    if ($timedOut) { throw 'Startup timed out; only this child process tree was stopped.' }
    if ($process.ExitCode -ne 0) { throw "Startup failed (exit $($process.ExitCode)); see stderr.log." }
    $cpu = Get-Content -LiteralPath $cpuPath -Raw | ConvertFrom-Json
    if ($cpu.droppedEvents -ne 0 -or -not @($cpu.traceEvents | Where-Object name -eq 'Startup.Ready').Count) { throw 'CPU capture incomplete.' }
}
finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
    if ($recording) {
        & $wpr -stop $trace *> (Join-Path $OutputDirectory 'wpr-stop.log')
        if ($LASTEXITCODE -ne 0) { throw 'WPR stop failed; inspect wpr-stop.log and recorder status.' }
    }
}
Write-Host "Native trace captured: $trace. Analyze event loss before using it."
