[CmdletBinding()]
param(
    [string]$Version,
    [string]$Tag,
    [ValidateSet('All', 'NuGet', 'Apps')][string]$Target = 'All',
    [ValidateSet('R2R', 'Aot', 'Both')][string]$SampleMode = 'R2R',
    [string]$OutputRoot,
    [switch]$SkipPackageValidation
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Version = Get-ReleaseVersion $Version $Tag
if ($SampleMode -ne 'R2R') {
    throw 'Desktop Sample NativeAOT is not release-ready: strict native linking reports JSON, reflection binding, DI and third-party trimming errors. Run scripts/release/Test-DesktopAot.ps1 for the diagnostic publish. R2R is the verified release mode; no fallback was performed.'
}
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot 'out/release' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versionRoot = Join-Path $OutputRoot $Version
if (Test-Path -LiteralPath $versionRoot) { throw "Output already exists: $versionRoot. Use a fresh version or OutputRoot." }
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
if ($Target -in @('All', 'Apps')) {
    $contracts = Join-Path $work 'contracts'
    & (Join-Path $PSScriptRoot 'Publish.ps1') -Package All -Version $Version -OutputRoot (Join-Path $work 'apps') -ContractOutputDirectory $contracts -IncludeModeInName
    if (!$?) { throw 'Application packaging failed.' }
    Copy-Item (Join-Path $work "apps/$Version/*.zip") $assets
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $contracts 'LICENSE')
    @'
Sample authoring contracts. Copy this complete directory into the Editor project's Contracts directory, then reopen the project.
These IL DLLs match this release's Sample. They do not enable dynamic C# loading in a NativeAOT player.
'@ | Set-Content (Join-Path $contracts 'README.txt') -Encoding utf8
    $contractFiles = @(Get-ChildItem -LiteralPath $contracts -Filter '*.dll' | Sort-Object Name | ForEach-Object {
        [ordered]@{ path = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    if ($contractFiles.Count -ne 3) { throw 'Expected three Sample contract closure DLLs.' }
    [ordered]@{ version = $Version; sourceCommit = $commit; files = $contractFiles } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $contracts 'release-manifest.json') -Encoding utf8
    Write-ReleaseZip $contracts (Join-Path $assets "GalNet-Sample-Contracts-$Version.zip")
    $zipFiles = @(Get-ChildItem -LiteralPath $assets -Filter '*.zip')
    if ($zipFiles.Count -ne 4) { throw "Expected 4 ZIP files; found $($zipFiles.Count)." }
    & (Join-Path $PSScriptRoot 'Test-Release.ps1') -Version $Version -AssetDirectory $assets -WorkDirectory (Join-Path $work 'app-validation')
    if (!$?) { throw 'Release payload verification failed.' }
}
$files = @(Get-ChildItem -LiteralPath $candidate -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($candidate, $_.FullName).Replace('\', '/'); size = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$files | ForEach-Object { "$($_.sha256)  $($_.path)" } | Set-Content (Join-Path $candidate 'SHA256SUMS') -Encoding ascii
$index = [ordered]@{
    schemaVersion = 1; version = $Version; tag = $Tag; sourceCommit = $commit; dirty = $dirty
    target = $Target; runtime = 'win-x64'; sampleModes = @('r2r'); packageValidation = $packageValidation
    packageValidationSkipped = [bool]$SkipPackageValidation; files = $files; createdUtc = [DateTime]::UtcNow.ToString('O')
}
$index | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $candidate 'release-index.json') -Encoding utf8
# Only expose a release directory once every requested phase succeeded.
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
[IO.Directory]::Move($candidate, $versionRoot)
Write-Host "Local release complete: $versionRoot"
Write-Host "Evidence and logs: $work"
