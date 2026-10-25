[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$TraceTool = 'dotnet-trace',
    [string]$OutputDirectory,
    [string[]]$ApplicationArguments = @(),
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$traceCommand = (Get-Command $TraceTool -ErrorAction Stop).Source
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot ('out/benchmarks/profile-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$tracePath = Join-Path $OutputDirectory 'startup.nettrace'
$cpuPath = Join-Path $OutputDirectory 'startup.cpu.json'
if ((Test-Path -LiteralPath $tracePath) -or (Test-Path -LiteralPath $cpuPath)) { throw 'Capture already exists; use a new output directory.' }
$startInfo = [Diagnostics.ProcessStartInfo]::new($traceCommand)
$startInfo.WorkingDirectory = Split-Path -Parent $Executable
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.Environment['GALNET_BENCHMARK_OUTPUT'] = $cpuPath
$startInfo.Environment['GALNET_BENCHMARK_EXIT'] = '1'
$startInfo.Environment['GALNET_BENCHMARK_EVENTPIPE'] = '1'
foreach ($argument in @('collect', '--profile', 'dotnet-sampled-thread-time', '--providers',
    'Microsoft-Windows-DotNETRuntime:0x1C:5,GalNet-CPU:0xFFFFFFFFFFFFFFFF:4',
    '--output', $tracePath, '--', $Executable) + $ApplicationArguments) {
    $startInfo.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$started = $false
try {
    $null = $process.Start()
    $started = $true
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'trace.stdout.log')
    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'trace.stderr.log')
    if ($timedOut) { throw 'Trace capture timed out; only this capture process tree was stopped.' }
    if ($process.ExitCode -ne 0) { throw "dotnet-trace failed (exit $($process.ExitCode)); see capture logs." }
}
finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
if (-not (Test-Path -LiteralPath $tracePath)) { throw 'No runtime trace produced.' }
$cpu = Get-Content -LiteralPath $cpuPath -Raw | ConvertFrom-Json
if ($cpu.droppedEvents -ne 0 -or -not @($cpu.traceEvents | Where-Object name -eq 'Startup.Ready').Count) { throw 'CPU capture incomplete.' }
$assemblyPath = Join-Path (Split-Path $Executable) ([IO.Path]::GetFileNameWithoutExtension($Executable) + '.dll')
[ordered]@{
    Executable = $Executable
    AssemblySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash
    ToolVersion = (& $traceCommand --version)
    Commit = (& git -C $repositoryRoot rev-parse HEAD)
    Utc = [DateTime]::UtcNow.ToString('O')
    Providers = 'Microsoft-Windows-DotNETRuntime:0x1C:5,GalNet-CPU:0xFFFFFFFFFFFFFFFF:4'
    Profile = 'dotnet-sampled-thread-time'
    TimingUse = 'Diagnostic only; do not compare instrumented elapsed times as benchmark results.'
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'metadata.json')
Write-Host "Startup trace captured: $tracePath"
