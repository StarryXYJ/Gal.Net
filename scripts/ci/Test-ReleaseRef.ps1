[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '../..'),
    [string]$MainlineRef = 'refs/remotes/origin/master'
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-preview\.(0|[1-9][0-9]*))?$') {
    throw 'Invalid release tag: expected vX.Y.Z or vX.Y.Z-preview.N.'
}
$tagCommit = & git -C $RepositoryPath rev-parse --verify "refs/tags/$Tag^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Release tag does not resolve to a commit.' }
$headCommit = & git -C $RepositoryPath rev-parse --verify HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $headCommit) { throw 'Release tag must point exactly to the checked-out HEAD.' }
$mainlineCommit = & git -C $RepositoryPath rev-parse --verify "$MainlineRef^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Release main branch is missing; fetch origin/master before checking the tag.' }
& git -C $RepositoryPath merge-base --is-ancestor $tagCommit $mainlineCommit
if ($LASTEXITCODE -eq 1) { throw 'Release tag commit has not been merged into origin/master.' }
if ($LASTEXITCODE -ne 0) { throw 'Could not verify release tag ancestry.' }
Write-Host "Release tag $Tag is contained in origin/master."
