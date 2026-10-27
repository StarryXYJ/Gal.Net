[CmdletBinding()]
param([string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "out/desktop-aot/$([guid]::NewGuid().ToString('N'))" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$script:logRoot = Join-Path $OutputDirectory 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
Invoke-ReleaseCommand 'desktop-aot' 'dotnet' @('publish', (Join-Path $repoRoot 'src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', '--artifacts-path', (Join-Path $OutputDirectory 'artifacts'), '-o', (Join-Path $OutputDirectory 'publish'),
    '-p:PublishAot=true', '-p:EnableGameDebugging=false', '-p:RestoreLockedMode=false', '-p:RestorePackagesWithLockFile=false',
    "-p:NuGetLockFilePath=$(Join-Path $OutputDirectory 'probe.lock.json')", '-p:NuGetAudit=false', '-p:UseSharedCompilation=false',
    '-p:ILLinkTreatWarningsAsErrors=true', '-p:IlcTreatWarningsAsErrors=true', '-p:TrimmerSingleWarn=false', '-m:1', '-nr:false')
Write-Host 'Native compilation passed. Desktop runtime acceptance is still required before adding AOT to release artifacts.'
