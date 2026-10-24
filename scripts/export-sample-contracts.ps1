[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sampleProject = Join-Path $repositoryRoot 'src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj'
$sampleOutput = Join-Path $repositoryRoot "src/Samples/GalNet.Sample.Avalonia/bin/$Configuration/net10.0"
$contractsOutput = Join-Path $repositoryRoot "src/Samples/GalNet.Sample.Contracts/bin/$Configuration/net10.0"
$destination = Join-Path $repositoryRoot 'output/Contracts'

& dotnet build $sampleProject -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) { throw 'Sample build failed; no contracts exported.' }
$contractName = 'GalNet.Project.Contracts.dll'
if ((Get-FileHash -LiteralPath (Join-Path $sampleOutput $contractName)).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $contractsOutput $contractName)).Hash) {
    throw 'Sample and contract build outputs differ; no contracts exported.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $contractsOutput -Filter '*.dll' -File | Copy-Item -Destination $destination -Force
Write-Host "Contracts exported to: $destination"
Write-Host "Contracts SHA256: $((Get-FileHash -LiteralPath (Join-Path $destination $contractName) -Algorithm SHA256).Hash)"
Write-Host 'Close the Editor project, copy these DLLs to its Contracts directory, then reopen it.'
Write-Host "Matching App: $(Join-Path $sampleOutput 'GalNet.Sample.Avalonia.exe')"
