[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $repo "out/ci/release-guards-$([guid]::NewGuid().ToString('N'))" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$attempts = [Collections.Generic.List[string]]::new()
# Negative tests must never reach a real build or external publication command.
function gh { $attempts.Add('gh'); throw 'External command forbidden in guard tests.' }
function dotnet { $attempts.Add('dotnet'); throw 'Build command forbidden in guard tests.' }
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    $message = $null
    try { & $Action } catch { $message = $_.Exception.Message }
    if (!$message -or $message -notmatch $Pattern) { throw "Expected failure '$Pattern'; got '$message'." }
}
$releaseScript = Join-Path $repo 'scripts/release/Release.ps1'
$pushScript = Join-Path $repo 'scripts/release/Push.ps1'
Expect-Failure { & $releaseScript -Version '../escape' -OutputRoot $OutputRoot } 'Version must'
Expect-Failure { & $releaseScript -Version '99.99.99-dev.1' -SampleMode Both -OutputRoot $OutputRoot } 'not release-ready'
$existing = Join-Path $OutputRoot '99.99.99'
New-Item -ItemType Directory -Path $existing | Out-Null
'keep' | Set-Content (Join-Path $existing 'sentinel.txt')
Expect-Failure { & $releaseScript -Version '99.99.99' -OutputRoot $OutputRoot } 'already exists'
if ((Get-Content (Join-Path $existing 'sentinel.txt')).Trim() -ne 'keep') { throw 'Existing output was modified.' }
$indexPath = Join-Path $OutputRoot 'release-index.json'
$index = [ordered]@{ schemaVersion = 1; dirty = $true; tag = 'v99.99.99'; version = '99.99.99'; packageValidationSkipped = $false; packageValidation = @{ passed = $true }; files = @() }
$index | ConvertTo-Json -Depth 6 | Set-Content $indexPath
Expect-Failure { & $pushScript -Index $indexPath -Destination GitHub } 'clean, exact-tag'
$index.dirty = $false
$index.files = @(@{ path = '../escape.txt'; sha256 = 'invalid'; size = 0 })
$index | ConvertTo-Json -Depth 6 | Set-Content $indexPath
Expect-Failure { & $pushScript -Index $indexPath -Destination GitHub } 'escaped release directory'
$index.files = @(@{ path = '99.99.99/sentinel.txt'; sha256 = 'invalid'; size = 0 })
$index | ConvertTo-Json -Depth 6 | Set-Content $indexPath
Expect-Failure { & $pushScript -Index $indexPath -Destination GitHub } 'Release file changed'
$index.files = @(@{ path = '99.99.99/sentinel.txt'; sha256 = (Get-FileHash (Join-Path $existing 'sentinel.txt') -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item (Join-Path $existing 'sentinel.txt')).Length })
$index['sourceCommit'] = (& git -C $repo rev-parse HEAD).Trim()
$index | ConvertTo-Json -Depth 6 | Set-Content $indexPath
Expect-Failure { & $pushScript -Index $indexPath -Destination GitHub } 'tag commit does not match'

# Exercise the actual ancestry check in a disposable repository, without fetching or pushing.
$refScript = Join-Path $repo 'scripts/ci/Test-ReleaseRef.ps1'
$refRepo = Join-Path $OutputRoot 'tag-fixture'
New-Item -ItemType Directory -Path $refRepo | Out-Null
$gitCommand = (Get-Command git -CommandType Application | Select-Object -First 1).Source
function Invoke-FixtureGit([string[]]$Arguments) {
    $output = & $gitCommand -C $refRepo @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $Arguments / $output" }
    return ($output -join "`n").Trim()
}
Invoke-FixtureGit @('init', '--initial-branch=master') | Out-Null
Invoke-FixtureGit @('config', 'user.name', 'GalNet Release Guard') | Out-Null
Invoke-FixtureGit @('config', 'user.email', 'release-guard@example.invalid') | Out-Null
Invoke-FixtureGit @('config', 'commit.gpgSign', 'false') | Out-Null
Invoke-FixtureGit @('config', 'tag.gpgSign', 'false') | Out-Null
Invoke-FixtureGit @('config', 'core.hooksPath', (Join-Path $OutputRoot 'no-hooks')) | Out-Null
Invoke-FixtureGit @('commit', '--allow-empty', '-m', 'Mainline base') | Out-Null
$baseCommit = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('tag', 'v99.99.0') | Out-Null
Invoke-FixtureGit @('commit', '--allow-empty', '-m', 'Mainline next') | Out-Null
$mainCommit = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('tag', '-a', 'v99.99.1', '-m', 'Annotated release') | Out-Null
Invoke-FixtureGit @('tag', 'v99.99.1-preview.1') | Out-Null
Invoke-FixtureGit @('update-ref', 'refs/remotes/origin/master', $mainCommit) | Out-Null
& $refScript -Tag 'v99.99.1' -RepositoryPath $refRepo
& $refScript -Tag 'v99.99.1-preview.1' -RepositoryPath $refRepo
Expect-Failure { & $refScript -Tag 'v99.99.0' -RepositoryPath $refRepo } 'checked-out HEAD'
Invoke-FixtureGit @('checkout', '--detach', $baseCommit) | Out-Null
& $refScript -Tag 'v99.99.0' -RepositoryPath $refRepo
Invoke-FixtureGit @('checkout', '-b', 'unmerged') | Out-Null
Invoke-FixtureGit @('commit', '--allow-empty', '-m', 'Unmerged change') | Out-Null
Invoke-FixtureGit @('tag', 'v99.99.2') | Out-Null
Expect-Failure { & $refScript -Tag 'v99.99.2' -RepositoryPath $refRepo } 'has not been merged'
Expect-Failure { & $refScript -Tag 'v99.99.98' -RepositoryPath $refRepo } 'does not resolve'
Expect-Failure { & $refScript -Tag 'v99.99.2-dev.1' -RepositoryPath $refRepo } 'Invalid release tag'
Invoke-FixtureGit @('checkout', '--detach', $mainCommit) | Out-Null
Invoke-FixtureGit @('update-ref', '-d', 'refs/remotes/origin/master') | Out-Null
Expect-Failure { & $refScript -Tag 'v99.99.1' -RepositoryPath $refRepo } 'main branch is missing'

function Test-GitHubPayloadSet {
    $uploadCalls = [Collections.Generic.List[string]]::new()
    function gh { $uploadCalls.Add(($args -join ' ')); $global:LASTEXITCODE = 0 }
    $fixtureScripts = Join-Path $refRepo 'scripts/release'
    New-Item -ItemType Directory -Path $fixtureScripts -Force | Out-Null
    Copy-Item -LiteralPath $pushScript, (Join-Path $repo 'scripts/release/Common.ps1') -Destination $fixtureScripts
    Invoke-FixtureGit @('remote', 'add', 'origin', 'https://github.com/example/galnet-release-guard.git') | Out-Null
    $payloadRoot = Join-Path $refRepo 'out/release/99.99.1'
    $assetRoot = Join-Path $payloadRoot 'assets'
    New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
    $assetFiles = @(0..6 | ForEach-Object {
        $assetPath = Join-Path $assetRoot "asset-$_.zip"
        "fixture-$_" | Set-Content $assetPath
        @{ path = "assets/asset-$_.zip"; sha256 = (Get-FileHash $assetPath).Hash.ToLowerInvariant(); size = (Get-Item $assetPath).Length }
    })
    'fixture checksums' | Set-Content (Join-Path $payloadRoot 'SHA256SUMS')
    $payloadIndex = [ordered]@{ schemaVersion = 1; dirty = $false; tag = 'v99.99.1'; version = '99.99.1'; sourceCommit = $mainCommit; packageValidationSkipped = $false; files = @($assetFiles[0..3]) }
    $payloadIndexPath = Join-Path $payloadRoot 'release-index.json'
    $fixturePush = Join-Path $fixtureScripts 'Push.ps1'
    $payloadIndex | ConvertTo-Json -Depth 6 | Set-Content $payloadIndexPath
    Expect-Failure { & $fixturePush -Index $payloadIndexPath -Destination GitHub } 'Expected seven'
    if ($uploadCalls.Count -or (Test-Path (Join-Path $payloadRoot 'push-state.json'))) { throw 'Invalid payload count reached publication.' }
    $payloadIndex.files = $assetFiles
    $payloadIndex | ConvertTo-Json -Depth 6 | Set-Content $payloadIndexPath
    & $fixturePush -Index $payloadIndexPath -Destination GitHub
    if ($uploadCalls.Count -ne 11 -or $uploadCalls[-1] -notmatch '^release edit .*--draft=false') { throw 'Seven-asset publication sequence was not completed.' }
}
Test-GitHubPayloadSet
if ($attempts.Count) { throw "Guard tests attempted external commands: $($attempts -join ', ')" }
[ordered]@{ passed = $true; cases = 17; externalCommands = 0; releaseRefCases = 8; payloadCases = 2 } | ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'guard-results.json')
Write-Host 'Release guard tests passed (17 cases, no builds or uploads).'
