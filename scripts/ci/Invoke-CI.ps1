[CmdletBinding()]
param(
    [ValidateSet('All', 'Format', 'BuildTest')][string]$Stage = 'All',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [switch]$Coverage
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $repo "out/ci/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([guid]::NewGuid().ToString('N').Substring(0, 8))" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$results = [Collections.Generic.List[object]]::new()
$previousCI = $env:CI
$env:CI = 'true'
$succeeded = $false

function Invoke-Check([string]$Name, [string[]]$Arguments, [switch]$ContinueOnFailure) {
    $log = Join-Path $OutputRoot "$Name.log"
    Write-Host "[$Name] dotnet $($Arguments -join ' ')"
    & dotnet @Arguments *> $log
    $code = $LASTEXITCODE
    $results.Add([ordered]@{ name = $Name; exitCode = $code; log = $log })
    if ($code -ne 0) {
        Get-Content -LiteralPath $log -Tail 65 | Write-Host
        if ($ContinueOnFailure) { Write-Warning "CI check '$Name' failed ($code). Full log: $log"; return }
        throw "CI check '$Name' failed ($code). Full log: $log"
    }
    Get-Content -LiteralPath $log -Tail 3 | Write-Host
}

Push-Location $repo
try {
    $expected = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
    $actual = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) { throw "Expected .NET SDK $expected; selected $actual." }
    $guardRoot = Join-Path $OutputRoot "guards-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $guardLog = Join-Path $OutputRoot 'release-guards.log'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ReleaseGuards.ps1') -OutputRoot $guardRoot *> $guardLog
    $guardCode = $LASTEXITCODE
    $results.Add([ordered]@{ name = 'release-guards'; exitCode = $guardCode; log = $guardLog })
    if ($guardCode -ne 0) { throw "Release guard checks failed. Log: $guardLog" }
    # Configuration affects the Sample and integration restore graphs. Use it in every phase.
    Invoke-Check 'restore' @('restore', 'GalNet.slnx', '--locked-mode', "-p:Configuration=$Configuration", '-m:1', '-nr:false')
    if ($Stage -in @('All', 'Format')) {
        Invoke-Check 'whitespace' @('format', 'whitespace', 'GalNet.slnx', '--no-restore', '--verify-no-changes', '--verbosity', 'quiet')
        Invoke-Check 'imports' @('format', 'style', 'GalNet.slnx', '--no-restore', '--verify-no-changes', '--diagnostics', 'IDE0005', '--verbosity', 'quiet')
    }
    if ($Stage -in @('All', 'BuildTest')) {
        Invoke-Check 'build' @('build', 'GalNet.slnx', '-c', $Configuration, '--no-restore', '-m:1', '-nr:false', '-p:UseSharedCompilation=false')
        # Discover actual solution test projects, never generated fixture copies in obj/bin.
        [xml]$solution = Get-Content GalNet.slnx
        $projects = @($solution.SelectNodes('//Project') | ForEach-Object { $_.Path } |
            Where-Object { $_ -match '^test/GalNet\.[^/]+\.Tests/|^test/GalNet\.IntegrationTests/' } | Sort-Object)
        if ($projects.Count -lt 14) { throw "Unexpected test project count: $($projects.Count)." }
        foreach ($project in $projects) {
            $name = [IO.Path]::GetFileNameWithoutExtension($project)
            $arguments = @('test', $project, '-c', $Configuration, '--no-build', '--no-restore', '--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $OutputRoot 'test-results'))
            if ($Coverage) { $arguments += '--collect:XPlat Code Coverage' }
            Invoke-Check $name $arguments -ContinueOnFailure
        }
        $failed = @($results | Where-Object { $_.exitCode -ne 0 })
        if ($failed.Count) { throw "Failed CI checks: $(($failed | ForEach-Object name) -join ', '). Evidence: $OutputRoot" }
    }
    $succeeded = $true
}
finally {
    $report = [ordered]@{ schemaVersion = 1; passed = $succeeded; stage = $Stage; configuration = $Configuration; checks = @($results.ToArray()); completedUtc = [DateTime]::UtcNow.ToString('O') }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputRoot 'ci-results.json') -Encoding utf8
    $env:CI = $previousCI
    Pop-Location
    Write-Host "CI evidence: $OutputRoot"
}
