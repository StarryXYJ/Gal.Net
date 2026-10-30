[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Index,
    [ValidateSet('NuGet', 'GitHub')][string[]]$Destination = @('NuGet', 'GitHub')
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Index = (Resolve-Path -LiteralPath $Index).Path
$root = Split-Path $Index -Parent
$release = Get-Content -LiteralPath $Index -Raw | ConvertFrom-Json
if ($release.schemaVersion -notin @(1, 2) -or $release.dirty -or !$release.tag -or $release.tag -cne "v$($release.version)") { throw 'Push requires a clean, exact-tag release index.' }
if ($release.packageValidationSkipped -or ($Destination -contains 'NuGet' -and !$release.packageValidation.passed)) { throw 'NuGet consumer validation is required.' }
if ($Destination -contains 'NuGet' -and !$env:NUGET_API_KEY) { throw 'NUGET_API_KEY is required (a short-lived Trusted Publishing key is supported).' }
$remote = (& git -C $repoRoot remote get-url origin).Trim()
if ($remote -notmatch 'github\.com[:/](?<repository>[^/]+/[^/]+?)(?:\.git)?$') { throw 'Push currently requires a github.com origin remote.' }
$githubRepository = $Matches.repository
$paths = @{}
foreach ($file in $release.files) {
    if ([IO.Path]::IsPathRooted($file.path)) { throw 'Index paths must be relative.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
    if (!$path.StartsWith($root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Index path escaped release directory.' }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $file.sha256 -or (Get-Item $path).Length -ne $file.size) { throw "Release file changed: $($file.path)" }
    $paths[$file.path] = $path
}
$tagCommit = & git -C $repoRoot rev-parse --verify "$($release.tag)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $release.sourceCommit) { throw 'Release tag commit does not match the sealed source commit.' }
$assets = @($release.files | Where-Object { $_.path.StartsWith('assets/') } | ForEach-Object { $paths[$_.path] })
if ($Destination -contains 'GitHub') {
    if ($release.schemaVersion -eq 1 -and $assets.Count -ne 7) { throw 'Expected seven verified R2R/Contracts release ZIPs.' }
    if ($release.schemaVersion -eq 2) {
        $expectedAssets = @(
            foreach ($rid in @('win-x64', 'linux-x64')) {
                $extension = if ($rid -eq 'win-x64') { '.zip' } else { '.tar.gz' }
                foreach ($suffix in @('', '-framework-dependent')) {
                    foreach ($product in @('Editor', 'CrossPlatform-Sample-Developer', 'CrossPlatform-Sample-Player', 'CrossPlatform-Sample-GameTestCase')) {
                        "GalNet-$product-$($release.version)-$rid$suffix$extension"
                    }
                }
                "GalNet-CrossPlatform-Sample-Contracts-$($release.version)-$rid.zip"
            }
            "GalNet-CrossPlatform-Sample-GameTestCase-$($release.version)-android-arm64.apk"
        )
        $actualAssets = @($assets | ForEach-Object { [IO.Path]::GetFileName($_) })
        if ($actualAssets.Count -ne $expectedAssets.Count -or @(Compare-Object $expectedAssets $actualAssets).Count) {
            throw 'Expected the complete cross-platform release matrix (19 application/contract assets).'
        }
    }
}
$indexHash = (Get-FileHash $Index -Algorithm SHA256).Hash
$statePath = Join-Path $root 'push-state.json'
$state = @{ indexHash = $indexHash; completed = @() }
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content $statePath -Raw | ConvertFrom-Json -AsHashtable
    if ($state.indexHash -cne $indexHash) { throw 'Push journal belongs to a different index.' }
}
function Save-PushStep([string]$Step) {
    $state.completed += $Step
    $state | ConvertTo-Json -Depth 4 | Set-Content $statePath -Encoding utf8
}
if ($Destination -contains 'GitHub' -and $state.completed -notcontains 'github-draft') {
    & gh release create $release.tag --repo $githubRepository --verify-tag --draft --title "GalNet $($release.version)" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'Creating release draft failed. Existing releases are not overwritten.' }
    Save-PushStep 'github-draft'
}
if ($Destination -contains 'NuGet') {
    $mapping = Get-Content (Join-Path $repoRoot 'packaging/packages.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($id in $mapping.Keys) {
        $step = "nuget-$id"
        if ($state.completed -contains $step) { continue }
        $relative = "nuget/$id.$($release.version).nupkg"
        if (!$paths.ContainsKey($relative)) { throw "Missing $relative" }
        # Never skip an unknown duplicate: a version conflict needs explicit investigation.
        & dotnet nuget push $paths[$relative] --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
        if ($LASTEXITCODE -ne 0) { throw "NuGet push failed: $id. Resume using the same index and push journal." }
        Save-PushStep $step
    }
}
if ($Destination -contains 'GitHub') {
    if ($state.completed -notcontains 'github-assets') {
        foreach ($asset in @($assets) + @($Index, (Join-Path $root 'SHA256SUMS'))) {
            $step = 'github-asset-' + [IO.Path]::GetFileName($asset)
            if ($state.completed -contains $step) { continue }
            & gh release upload $release.tag $asset --repo $githubRepository
            if ($LASTEXITCODE -ne 0) { throw "Uploading release asset failed: $asset" }
            Save-PushStep $step
        }
        Save-PushStep 'github-assets'
    }
    if ($state.completed -notcontains 'github-published') {
        $arguments = @('release', 'edit', $release.tag, '--repo', $githubRepository, '--draft=false')
        if ($release.version -match '-preview\.') { $arguments += '--prerelease' }
        & gh @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Publishing release draft failed.' }
        Save-PushStep 'github-published'
    }
}
Write-Host "Upload completed. Journal: $statePath"
