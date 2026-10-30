[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(-preview\.[0-9]+)?$')][string]$Tag,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Token = $env:GH_TOKEN,
    [string]$GitHubOutput
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or !$Token) { throw 'GitHub repository and token are required.' }
$api = if ($env:GITHUB_API_URL) { $env:GITHUB_API_URL } else { 'https://api.github.com' }
$published = $false
try {
    $release = Invoke-RestMethod -Uri "$api/repos/$Repository/releases/tags/$Tag" -Headers @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json' } -TimeoutSec 30
    if ($release.draft) { throw "Release $Tag has an unfinished draft. Resume using the preserved original files and push journal." }
    $published = $true
}
catch {
    if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
Write-ActionOutput 'published' $published.ToString().ToLowerInvariant() $GitHubOutput
Write-Host "Release $Tag already published: $published."
$published
