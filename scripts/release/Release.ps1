[CmdletBinding()]
param(
    [string]$Version,
    [string]$Tag,
    [ValidateSet('All', 'NuGet', 'Apps')][string]$Target = 'NuGet',
    [ValidateSet('R2R', 'Aot', 'Both')][string]$SampleMode = 'R2R',
    [ValidateSet('Player', 'Developer')][string]$SampleFlavor = 'Player',
    [string]$OutputRoot,
    [switch]$SkipPackageValidation
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Version = Get-ReleaseVersion $Version $Tag
if ($SampleMode -ne 'R2R') {
    throw 'Desktop Sample NativeAOT is not release-ready: strict native linking reports JSON, reflection binding, DI and third-party trimming errors. Use the CrossPlatform application release pipeline; no fallback was performed.'
}
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot 'out/release' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versionRoot = Join-Path $OutputRoot $Version
if (Test-Path -LiteralPath $versionRoot) { throw "Output already exists: $versionRoot. Use a fresh version or OutputRoot." }
if ($Target -in @('All', 'Apps')) { throw 'Applications use scripts/ci/publish-applications.py and scripts/release/cross-platform-release.py.' }
$work = Join-Path $OutputRoot ".work/$Version-$([guid]::NewGuid().ToString('N'))"
$script:logRoot = Join-Path $work 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$candidate = Join-Path $work 'candidate'
$nuget = Join-Path $candidate 'nuget'
$assets = Join-Path $candidate 'assets'
New-Item -ItemType Directory -Force -Path $nuget, $assets | Out-Null
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repoRoot status --porcelain)
$packageValidation = $null
if ($Target -in @('All', 'NuGet')) {
    & (Join-Path $PSScriptRoot 'Pack-NuGet.ps1') -Version $Version -OutputDirectory $nuget -WorkDirectory (Join-Path $work 'pack')
    if (!$?) { throw 'NuGet packing failed.' }
    if (!$SkipPackageValidation) {
        & (Join-Path $PSScriptRoot 'Test-NuGet.ps1') -Version $Version -PackageDirectory $nuget -WorkDirectory (Join-Path $work 'validation')
        if (!$?) { throw 'NuGet validation failed.' }
        $packageValidation = Get-Content (Join-Path $work 'validation/package-validation.json') -Raw | ConvertFrom-Json
    }
}
$files = @(Get-ChildItem -LiteralPath $candidate -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($candidate, $_.FullName).Replace('\', '/'); size = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$files | ForEach-Object { "$($_.sha256)  $($_.path)" } | Set-Content (Join-Path $candidate 'SHA256SUMS') -Encoding ascii
$index = [ordered]@{
    schemaVersion = 1; version = $Version; tag = $Tag; sourceCommit = $commit; dirty = $dirty
    target = $Target; runtime = 'nuget'; sampleModes = @(); sampleFlavor = $null; deployments = @(); packageValidation = $packageValidation
    packageValidationSkipped = [bool]$SkipPackageValidation; files = $files; createdUtc = [DateTime]::UtcNow.ToString('O')
}
$index | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $candidate 'release-index.json') -Encoding utf8
# Only expose a release directory once every requested phase succeeded.
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
[IO.Directory]::Move($candidate, $versionRoot)
Write-Host "Local release complete: $versionRoot"
Write-Host "Evidence and logs: $work"
