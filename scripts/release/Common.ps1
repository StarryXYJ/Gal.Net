$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Invoke-ReleaseCommand([string]$Name, [string]$Command, [string[]]$Arguments) {
    $log = Join-Path $script:logRoot "$Name.log"
    Write-Host "[$Name] $Command $($Arguments -join ' ')"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Command @Arguments *> $log
    $code = $LASTEXITCODE
    [ordered]@{ name = $Name; exitCode = $code; elapsedSeconds = $timer.Elapsed.TotalSeconds } |
        ConvertTo-Json -Compress | Add-Content (Join-Path $script:logRoot 'timings.jsonl') -Encoding utf8
    if ($code -ne 0) {
        Get-Content -LiteralPath $log -Tail 60 | Write-Host
        throw "$Name failed ($code). Log: $log"
    }
}

function Get-ReleaseVersion([string]$Version, [string]$Tag) {
    if ($Tag) {
        if ($Version) { throw 'Pass Version or Tag, not both.' }
        if ($Tag -notmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-preview\.(0|[1-9][0-9]*))?$') { throw 'Invalid release tag.' }
        $tagCommit = & git -C $repoRoot rev-parse "$Tag^{commit}"
        if ($LASTEXITCODE -ne 0) { throw 'Tag does not exist.' }
        $headCommit = & git -C $repoRoot rev-parse HEAD
        if ($tagCommit -ne $headCommit) { throw 'Release tag must point exactly to HEAD.' }
        if (& git -C $repoRoot status --porcelain) { throw 'Tag releases require a clean working tree.' }
        return $Tag.Substring(1)
    }
    if (!$Version) {
        $count = & git -C $repoRoot rev-list --count HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine local version.' }
        $Version = "0.0.0-dev.$count"
    }
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(preview|dev)\.(0|[1-9][0-9]*))?$') { throw 'Version must be x.y.z, x.y.z-preview.N or x.y.z-dev.N.' }
    return $Version
}

function Write-ReleaseZip([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite $Destination" }
    [IO.Compression.ZipFile]::CreateFromDirectory($Source, $Destination, [IO.Compression.CompressionLevel]::Optimal, $false)
}
