[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$OutputRoot) { $OutputRoot = Join-Path $repository "out/ci/release-plan-$([guid]::NewGuid().ToString('N'))" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$cases = [Collections.Generic.List[string]]::new()
function Assert-Plan([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "Release plan failed: $Name" }
    $cases.Add($Name)
}
function Expect-PlanFailure([scriptblock]$Action, [string]$Pattern, [string]$Name) {
    $message = ''
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Assert-Plan ($message -match $Pattern) $Name
}
$filter = New-DesktopSolutionFilter -Path (Join-Path $OutputRoot 'desktop.slnf')
$filtered = (Get-Content $filter -Raw | ConvertFrom-Json).solution.projects
Assert-Plan (!@($filtered | Where-Object { $_ -match '/GalNet.Sample.CrossPlatform/' }).Count) 'Desktop filter excludes mobile and browser workloads'
Assert-Plan (@($filtered | Where-Object { $_ -like 'test/Fixtures/*' }).Count -ge 7) 'Desktop filter retains solution fixtures'
[xml]$originalSolution = Get-Content (Join-Path $repository 'GalNet.slnx')
$originalTests = @($originalSolution.SelectNodes('//Project') | ForEach-Object { $_.Path } | Where-Object { $_ -match '^test/GalNet\.' })
Assert-Plan (!@($originalTests | Where-Object { $_ -cnotin $filtered }).Count -and $originalTests.Count -ge 14) 'Desktop filter retains every solution test project'
Expect-PlanFailure { New-DesktopSolutionFilter -Path (Join-Path $OutputRoot 'invalid.slnf') -Projects @('unknown.csproj') } 'outside the desktop' 'Unknown filter project rejected'

# Real Git history verifies tags on source commits, rather than only on merge HEAD.
$gitRoot = Join-Path $OutputRoot "git-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $gitRoot | Out-Null
function Invoke-PlanFixtureGit([string[]]$Arguments) {
    $value = & git -C $gitRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $Arguments / $value" }
    return ($value -join "`n").Trim()
}
Invoke-PlanFixtureGit @('init', '--initial-branch=master') | Out-Null
Invoke-PlanFixtureGit @('config', 'user.name', 'Release Plan Fixture') | Out-Null
Invoke-PlanFixtureGit @('config', 'user.email', 'release-plan@example.invalid') | Out-Null
Invoke-PlanFixtureGit @('config', 'commit.gpgSign', 'false') | Out-Null
Invoke-PlanFixtureGit @('config', 'tag.gpgSign', 'false') | Out-Null
Invoke-PlanFixtureGit @('config', 'core.hooksPath', (Join-Path $gitRoot 'no-hooks')) | Out-Null
Invoke-PlanFixtureGit @('commit', '--allow-empty', '-m', 'base') | Out-Null
$base = Invoke-PlanFixtureGit @('rev-parse', 'HEAD')
Invoke-PlanFixtureGit @('tag', 'v1.0.0') | Out-Null
Invoke-PlanFixtureGit @('update-ref', 'refs/remotes/origin/master', $base) | Out-Null
Invoke-PlanFixtureGit @('checkout', '-b', 'feature') | Out-Null
Invoke-PlanFixtureGit @('commit', '--allow-empty', '-m', 'feature') | Out-Null
$source = Invoke-PlanFixtureGit @('rev-parse', 'HEAD')
Invoke-PlanFixtureGit @('tag', '-a', 'v1.1.0', '-m', 'before merge') | Out-Null
Invoke-PlanFixtureGit @('tag', 'v1.1.0-preview.1') | Out-Null
$eventPath = Join-Path $OutputRoot 'event.json'
function Get-FixtureReleases($EventData) {
    $EventData | ConvertTo-Json -Depth 5 | Set-Content $eventPath -Encoding utf8
    & (Join-Path $PSScriptRoot 'Get-ReleasePlan.ps1') -EventPath $eventPath -RepositoryPath $gitRoot
}
Assert-Plan ((Get-FixtureReleases @{ ref = 'refs/tags/v1.1.0' }).include.Count -eq 0) 'Tag before merge waits'
Invoke-PlanFixtureGit @('checkout', 'master') | Out-Null
Invoke-PlanFixtureGit @('commit', '--allow-empty', '-m', 'master advance') | Out-Null
$before = Invoke-PlanFixtureGit @('rev-parse', 'HEAD')
Invoke-PlanFixtureGit @('update-ref', 'refs/remotes/origin/master', $before) | Out-Null
Assert-Plan ((Get-FixtureReleases @{ ref = 'refs/heads/master'; before = $base; after = $before }).include.Count -eq 0) 'Untagged master push does not release'
Invoke-PlanFixtureGit @('merge', '--no-ff', 'feature', '-m', 'merge feature') | Out-Null
$merged = Invoke-PlanFixtureGit @('rev-parse', 'HEAD')
Invoke-PlanFixtureGit @('update-ref', 'refs/remotes/origin/master', $merged) | Out-Null
$onMerge = Get-FixtureReleases @{ ref = 'refs/heads/master'; before = $before; after = $merged }
Assert-Plan ($onMerge.include.Count -eq 2) 'Merge discovers both newly eligible tags'
Assert-Plan (@($onMerge.include | Where-Object { $_.commit -ceq $source }).Count -eq 2 -and $source -cne $merged) 'Release uses exact tagged source rather than merge HEAD'
Assert-Plan (!@($onMerge.include | Where-Object { $_.tag -eq 'v1.0.0' }).Count) 'Historical versions excluded from master push'
Assert-Plan ((Get-FixtureReleases @{ ref = 'refs/tags/v1.0.0' }).include.Count -eq 1) 'Late tag on master history is eligible'
Assert-Plan ((Get-FixtureReleases @{ ref = 'refs/tags/v1.1.0' }).include.Count -eq 1) 'Tag after merge is eligible'
Expect-PlanFailure { Get-FixtureReleases @{ ref = 'refs/tags/v1.1.0-dev.1' } } 'Invalid release tag' 'Invalid version rejected'
Assert-Plan ((Get-FixtureReleases @{ ref = 'refs/tags/v1.1.0'; deleted = $true }).include.Count -eq 0) 'Tag deletion ignored'
$candidate = Get-FixtureReleases @{ ref = 'refs/heads/master'; inputs = @{ version = '1.2.0-dev.1' } }
Assert-Plan ($candidate.include.Count -eq 1 -and !$candidate.include[0].tag -and $candidate.include[0].commit -ceq $merged) 'Manual candidate has no publish tag'
Invoke-PlanFixtureGit @('checkout', '--detach', $source) | Out-Null
& (Join-Path $PSScriptRoot 'Test-ReleaseRef.ps1') -Tag 'v1.1.0' -RepositoryPath $gitRoot
Assert-Plan ($true) 'Exact source checkout passes existing release guard'

# API fixtures never contact GitHub or publish anything.
$script:releaseFixtureState = 'published'
function Invoke-RestMethod {
    param($Uri, $Headers, $TimeoutSec)
    switch ($releaseFixtureState) {
        'published' { return @{ draft = $false } }
        'draft' { return @{ draft = $true } }
        'missing' {
            $exception = [Exception]::new('Fixture not found')
            $exception | Add-Member -NotePropertyName Response -NotePropertyValue @{ StatusCode = 404 }
            throw $exception
        }
        default { throw 'Fixture API unavailable' }
    }
}
$stateScript = Join-Path $PSScriptRoot 'Get-ReleaseState.ps1'
Assert-Plan (& $stateScript -Tag 'v1.1.0' -Repository 'fixture/repo' -Token 'fixture-only') 'Published release is skipped'
$script:releaseFixtureState = 'missing'
Assert-Plan (!(& $stateScript -Tag 'v1.1.0' -Repository 'fixture/repo' -Token 'fixture-only')) 'Missing release is built'
$script:releaseFixtureState = 'draft'
Expect-PlanFailure { & $stateScript -Tag 'v1.1.0' -Repository 'fixture/repo' -Token 'fixture-only' } 'unfinished draft' 'Unfinished draft requires original journal'
$script:releaseFixtureState = 'error'
Expect-PlanFailure { & $stateScript -Tag 'v1.1.0' -Repository 'fixture/repo' -Token 'fixture-only' } 'unavailable' 'API failure is not treated as absence'
[ordered]@{ passed = $true; cases = $cases.Count; checks = @($cases.ToArray()); externalCalls = 0 } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputRoot 'plan-results.json') -Encoding utf8
Write-Host "Release plan tests passed ($($cases.Count) cases, no builds or uploads)."
