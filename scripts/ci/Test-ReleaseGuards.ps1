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
if ($attempts.Count) { throw "Guard tests attempted external commands: $($attempts -join ', ')" }
[ordered]@{ passed = $true; cases = 7; externalCommands = 0 } | ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'guard-results.json')
Write-Host 'Release guard tests passed (7 cases, no builds or uploads).'
