[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj'
$assembly = Join-Path $repositoryRoot "src/Samples/GalNet.Sample.Contracts/bin/$Configuration/net10.0/GalNet.Project.Contracts.dll"
$expectedHash = $null
foreach ($revision in @('1111111111111111111111111111111111111111', '2222222222222222222222222222222222222222')) {
    & dotnet build $project -c $Configuration --no-restore -t:Rebuild -m:1 "-p:SourceRevisionId=$revision" -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Contract rebuild failed for revision $revision." }
    $actualHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    Write-Host "Revision ${revision}: $actualHash"
    if ($null -ne $expectedHash -and $actualHash -ne $expectedHash) {
        throw 'Unchanged contract sources produced different DLL hashes when only SourceRevisionId changed.'
    }
    $expectedHash = $actualHash
}
Write-Host "Contract hash is stable across SourceRevisionId changes: $expectedHash"
