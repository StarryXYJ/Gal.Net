param(
    [string]$RuntimeIdentifier = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($RuntimeIdentifier -notmatch '^[a-z0-9-]+$') { throw 'Invalid runtime identifier.' }
$repository = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository ".galnet/native-aot-acceptance/$RuntimeIdentifier" }
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$publishDirectory = Join-Path $outputRoot 'publish'
$project = Join-Path $repository 'src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj'
# Keep RID/compiler restore inputs away from ordinary development lock files.
$arguments = @('publish', $project, '-c', 'Release', '-r', $RuntimeIdentifier, '--self-contained',
    '--artifacts-path', (Join-Path $outputRoot 'artifacts'), '-o', $publishDirectory, '-m:1', '-nr:false',
    '-p:PublishAot=true', '-p:UseSharedCompilation=false', '-p:IlcTreatWarningsAsErrors=true',
    '-p:ILLinkTreatWarningsAsErrors=true', '-p:RestorePackagesWithLockFile=false', '-p:RestoreLockedMode=false',
    "-p:NuGetLockFilePath=$(Join-Path $outputRoot 'unused.lock.json')")
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed: $LASTEXITCODE" }
$executable = Join-Path $publishDirectory ('GalNet.Sample.Headless' + $(if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }))
& $executable --aot-self-test --expect-aot
if ($LASTEXITCODE -ne 0) { throw "NativeAOT acceptance failed: $LASTEXITCODE" }
