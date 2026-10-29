[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EventPath,
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '../..'),
    [string]$MainlineRef = 'refs/remotes/origin/master',
    [string]$OutputPath,
    [string]$GitHubOutput
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$event = Get-Content -LiteralPath $EventPath -Raw | ConvertFrom-Json
$pattern = '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-preview\.(0|[1-9][0-9]*))?$'
$candidates = [Collections.Generic.List[object]]::new()
function Invoke-PlanGit([string[]]$Arguments) {
    $value = & git -C $RepositoryPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Release discovery git failed: $($Arguments -join ' ')" }
    return $value
}
function Test-PlanAncestor([string]$Commit, [string]$Ref) {
    & git -C $RepositoryPath merge-base --is-ancestor $Commit $Ref
    if ($LASTEXITCODE -gt 1) { throw 'Could not check release ancestry.' }
    return $LASTEXITCODE -eq 0
}
if ($event.inputs.version) {
    $version = $event.inputs.version
    if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(preview\.[0-9]+|dev(?:\.[0-9]+)?))?$') { throw 'Invalid candidate version.' }
    $candidates.Add(@{ tag = ''; version = $version; commit = (Invoke-PlanGit @('rev-parse', 'HEAD')) })
}
elseif ($event.ref -like 'refs/tags/*' -and !$event.deleted) {
    $tag = $event.ref.Substring('refs/tags/'.Length)
    if ($tag -cnotmatch $pattern) { throw 'Invalid release tag: expected vX.Y.Z or vX.Y.Z-preview.N.' }
    $commit = Invoke-PlanGit @('rev-parse', '--verify', "refs/tags/$tag^{commit}")
    if (Test-PlanAncestor $commit $MainlineRef) {
        $candidates.Add(@{ tag = $tag; version = $tag.Substring(1); commit = $commit })
    }
    else { Write-Host "Tag $tag is waiting to be merged into master." }
}
elseif ($event.ref -eq 'refs/heads/master' -and !$event.deleted) {
    if ($event.after -notmatch '^[a-f0-9]{40}$' -or $event.before -notmatch '^[a-f0-9]{40}$') { throw 'Invalid master push range.' }
    foreach ($tag in @(Invoke-PlanGit @('tag', '--merged', $event.after, '--list', 'v*'))) {
        if ($tag -cnotmatch $pattern) { continue }
        $commit = Invoke-PlanGit @('rev-parse', '--verify', "refs/tags/$tag^{commit}")
        if ($event.before -ne ('0' * 40) -and (Test-PlanAncestor $commit $event.before)) { continue }
        if (!(Test-PlanAncestor $commit $MainlineRef)) { throw 'New release commit is no longer on master.' }
        $candidates.Add(@{ tag = $tag; version = $tag.Substring(1); commit = $commit })
    }
}
$plan = [ordered]@{ include = @($candidates.ToArray()) }
$json = $plan | ConvertTo-Json -Depth 5 -Compress
if ($OutputPath) { $json | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
Write-ActionOutput 'matrix' $json $GitHubOutput
Write-ActionOutput 'has-releases' ($candidates.Count -gt 0).ToString().ToLowerInvariant() $GitHubOutput
Write-Host "Release candidates: $($candidates.Count)."
[pscustomobject]$plan
