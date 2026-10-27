[CmdletBinding()]
param(
    [ValidateSet('Editor', 'Sample', 'SampleIntegrated', 'All')]
    [string]$Package = 'All',
    [string]$Version,
    [string]$OutputRoot,
    [string]$ProjectDirectory,
    [string]$Galpak,
    [string]$ContractOutputDirectory,
    [switch]$IncludeModeInName
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$headlessProject = Join-Path $repoRoot 'src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $tag = (& git -C $repoRoot describe --tags --exact-match --match 'v[0-9]*' 2>$null)
    if ($LASTEXITCODE -eq 0 -and $tag -match '^v(.+)$') { $Version = $Matches[1] }
    else { $Version = '0.1.0-dev' }
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-(?:preview\.[0-9]+|dev(?:\.[0-9]+)?))?$') {
    throw "Version '$Version' must use MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-preview.N (dev is allowed for local builds)."
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repoRoot 'out/release' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versionRoot = Join-Path $OutputRoot $Version
$stagingRoot = Join-Path $OutputRoot '.staging'
$dotnetLogsRoot = Join-Path $stagingRoot 'logs'
New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dotnetLogsRoot -Force | Out-Null

if ($Package -in @('SampleIntegrated', 'All') -and [string]::IsNullOrWhiteSpace($Galpak) -and [string]::IsNullOrWhiteSpace($ProjectDirectory)) {
    $ProjectDirectory = Join-Path $repoRoot 'GameTestCase'
}
if (![string]::IsNullOrWhiteSpace($Galpak)) {
    $Galpak = (Resolve-Path -LiteralPath $Galpak).Path
    if (![string]::Equals([IO.Path]::GetExtension($Galpak), '.galpak', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The integration input must be a .galpak file.'
    }
}
if (![string]::IsNullOrWhiteSpace($ProjectDirectory)) {
    $ProjectDirectory = (Resolve-Path -LiteralPath $ProjectDirectory).Path
    if (!(Test-Path -LiteralPath (Join-Path $ProjectDirectory 'Graph/graph.json'))) {
        throw "ProjectDirectory does not contain Graph/graph.json: $ProjectDirectory"
    }
}

function Invoke-DotNet([string[]]$Arguments) {
    # Runtime CLI arguments must not receive MSBuild properties.
    $properties = @("-p:Version=$Version", '-p:ContinuousIntegrationBuild=true')
    $separator = [Array]::IndexOf($Arguments, '--')
    if ($separator -ge 0) {
        $Arguments = @($Arguments[0..($separator - 1)]) + $properties + @($Arguments[$separator..($Arguments.Length - 1)])
    }
    else { $Arguments += $properties }
    $logPath = Join-Path $dotnetLogsRoot "dotnet-$([guid]::NewGuid().ToString('N')).log"
    Write-Host "[apps] dotnet $($Arguments[0]) $($Arguments[1])"
    & dotnet @Arguments *> $logPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = Get-Content -LiteralPath $logPath -Tail 80 | Out-String
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode. Log: $logPath`n$details"
    }
    Write-Host "[apps] completed; log: $logPath"
}

function Get-RelativePath([string]$BasePath, [string]$Path) {
    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($Path)
    $baseUri = [Uri]::new($base)
    $targetUri = [Uri]::new($target)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function Copy-ContentTree([string]$Source, [string]$Target, [string[]]$Names) {
    foreach ($name in $Names) {
        $sourcePath = Join-Path $Source $name
        if (!(Test-Path -LiteralPath $sourcePath)) { continue }
        if ((Get-Item -LiteralPath $sourcePath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Content cannot be a symbolic link: $sourcePath"
        }
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $targetPath = Join-Path $Target $name
            New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath
            continue
        }

        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($sourcePath)
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Content cannot contain symbolic links: $directory"
            }
            $relativeDirectory = Get-RelativePath $Source $directory
            New-Item -ItemType Directory -Path (Join-Path $Target $relativeDirectory) -Force | Out-Null
            foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    throw "Content cannot contain symbolic links: $($item.FullName)"
                }
                if ($item.PSIsContainer) { $pending.Push($item.FullName); continue }
                $relativeFile = Get-RelativePath $Source $item.FullName
                $destination = Join-Path $Target $relativeFile
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $item.FullName -Destination $destination
            }
        }
    }
}

function Copy-PublishedFiles([string]$Source, [string]$Target, [string[]]$ExcludedRootFiles = @()) {
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse) {
        $relative = Get-RelativePath $Source $file.FullName
        if ($relative.IndexOf([IO.Path]::DirectorySeparatorChar) -lt 0 -and $ExcludedRootFiles -contains $relative) { continue }
        $destination = Join-Path $Target $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}

function Add-PackageManifest([string]$PackageRoot, [string]$Kind) {
    $files = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse |
        Sort-Object { (Get-RelativePath $PackageRoot $_.FullName).Replace('\', '/') } |
        ForEach-Object {
            $relative = (Get-RelativePath $PackageRoot $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            [ordered]@{ path = $relative; size = $_.Length; sha256 = $hash }
        })
    $commit = (& git -C $repoRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = $null }
    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'GalNet'
        version = $Version
        package = $Kind
        runtime = 'win-x64'
        selfContained = $true
        readyToRun = $true
        sourceCommit = $commit
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PackageRoot 'release-manifest.json') -Encoding utf8
}

function Complete-Package([string]$Kind, [string]$PackageRoot) {
    # Some native NuGet content ignores the publish RID and symbol-copy setting.
    # Filter the final payload, including the nested CLI, before hashing it.
    $payloadRoot = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $unwanted = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File | Where-Object {
        $_.Extension -eq '.pdb' -or
        (Get-RelativePath $PackageRoot $_.FullName).Replace('\', '/') -match '(^|/)libvlc/win-(x86|arm64)/'
    })
    foreach ($file in $unwanted) {
        if (!$file.FullName.StartsWith($payloadRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a file outside the release payload: $($file.FullName)"
        }
        Remove-Item -LiteralPath $file.FullName -Force
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $PackageRoot 'LICENSE') -Force
    Add-PackageManifest $PackageRoot $Kind
    $modeSuffix = if ($IncludeModeInName) { '-r2r' } else { '' }
    $zipName = "GalNet-$Kind-$Version-win-x64$modeSuffix.zip"
    $zipPath = Join-Path $versionRoot $zipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    if (Test-Path -LiteralPath "$zipPath.sha256") { Remove-Item -LiteralPath "$zipPath.sha256" -Force }
    Compress-Archive -Path (Join-Path $PackageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $zipName" -Encoding ascii
    Write-Output $zipPath
}

function Publish-Editor {
    $stage = Join-Path $stagingRoot "$Version-Editor-$([guid]::NewGuid().ToString('N'))"
    $hostRoot = Join-Path $stage 'payload'
    New-Item -ItemType Directory -Path $hostRoot -Force | Out-Null
    try {
        $project = Join-Path $repoRoot 'src/Editor/GalNet.Editor/GalNet.Editor.csproj'
        Invoke-DotNet @('restore', $project, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', $hostRoot)
        Invoke-DotNet @('restore', $headlessProject, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $headlessProject, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', (Join-Path $hostRoot 'CLI'))
        @'
GalNet Editor

Run GalNet.Editor.exe to start the editor.
'@ | Set-Content -LiteralPath (Join-Path $hostRoot 'README.txt') -Encoding utf8
        Complete-Package 'Editor' $hostRoot
    }
    finally { Remove-Stage $stage }
}

function Add-GameContent([string]$Stage, [string]$PackageRoot, [string]$ContractsRoot) {
    $contentBuildRoot = Join-Path $Stage 'GameTestCase'
    $contentRoot = Join-Path $PackageRoot 'GameTestCase'
    if (![string]::IsNullOrWhiteSpace($Galpak)) {
        Invoke-DotNet @('run', '--project', $headlessProject, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore', '--', 'galpak', 'install', $Galpak, '--output', $contentRoot)
        return
    }
    if ([string]::IsNullOrWhiteSpace($ProjectDirectory)) { throw 'Pass either -ProjectDirectory or -Galpak for SampleIntegrated.' }
    New-Item -ItemType Directory -Path $contentBuildRoot -Force | Out-Null
    Copy-ContentTree $ProjectDirectory $contentBuildRoot @('Assets', 'Graph', 'I18n', 'settings.json')
    $isDefaultSample = [IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\') -eq [IO.Path]::GetFullPath((Join-Path $repoRoot 'GameTestCase')).TrimEnd('\')
    if (!$isDefaultSample -and (Test-Path -LiteralPath (Join-Path $ProjectDirectory 'Contracts'))) {
        Copy-ContentTree $ProjectDirectory $contentBuildRoot @('Contracts')
    }
    else {
        $targetContracts = Join-Path $contentBuildRoot 'Contracts'
        New-Item -ItemType Directory -Force -Path $targetContracts | Out-Null
        foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
            Copy-Item -LiteralPath (Join-Path $ContractsRoot $name) -Destination $targetContracts
        }
    }
    Invoke-DotNet @('run', '--project', $headlessProject, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore', '--', 'project', 'export-directory', $contentBuildRoot, '--output', $contentRoot)
}

function Publish-SamplePackages([switch]$IncludePure, [switch]$IncludeIntegrated) {
    $stage = Join-Path $stagingRoot "$Version-Sample-$([guid]::NewGuid().ToString('N'))"
    $runtimeRoot = Join-Path $stage 'runtime'
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    try {
        $project = Join-Path $repoRoot 'src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj'
        Invoke-DotNet @('restore', $project, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '-p:EnableGameDebugging=false', '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', '-p:SelfContained=true', '-p:EnableGameDebugging=false', '-p:CopyOutputSymbolsToPublishDirectory=false', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', $runtimeRoot)

        if ($ContractOutputDirectory) {
            New-Item -ItemType Directory -Force -Path $ContractOutputDirectory | Out-Null
            foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
                Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $ContractOutputDirectory -Force
            }
        }

        if ($IncludePure) {
            @'
@echo off
setlocal
if "%~1"=="" (
  echo Usage: Run-Sample.cmd "path-to-game-content"
  exit /b 2
)
"%~dp0GalNet.Sample.Avalonia.exe" "%~f1" --profile "%LOCALAPPDATA%\GalNet\Sample\default"
'@ | Set-Content -LiteralPath (Join-Path $runtimeRoot 'Run-Sample.cmd') -Encoding ascii
            @'
GalNet Sample

This package contains the player runtime only. Supply a game content directory:
  Run-Sample.cmd "C:\path\to\expanded-game-content"

For packaged content, first expand the .galpak with the GalNet Editor or the
Headless CLI `galpak install` command.
'@ | Set-Content -LiteralPath (Join-Path $runtimeRoot 'README.txt') -Encoding utf8
            Complete-Package 'Sample' $runtimeRoot
        }

        if ($IncludeIntegrated) {
            $packageRoot = Join-Path $stage 'integrated-payload'
            New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
            $excluded = if ($IncludePure) { @('README.txt', 'Run-Sample.cmd', 'release-manifest.json') } else { @() }
            Copy-PublishedFiles $runtimeRoot $packageRoot $excluded
            Add-GameContent $stage $packageRoot $runtimeRoot
            @'
@echo off
setlocal
"%~dp0GalNet.Sample.Avalonia.exe" "%~dp0GameTestCase" --profile "%LOCALAPPDATA%\GalNet\Sample\GameTestCase"
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'Run-GameTestCase.cmd') -Encoding ascii
            @'
GalNet Sample with compiled GameTestCase content

Run Run-GameTestCase.cmd. Resources are compiled into Assets/Paks/*.pak,
and story groups into Graph/groups/*.galgroup. This is installed runtime
content; no authoring sources or .galpak extraction step are needed.
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'README.txt') -Encoding utf8
            Complete-Package 'Sample-GameTestCase' $packageRoot
        }
    }
    finally { Remove-Stage $stage }
}

function Remove-Stage([string]$Stage) {
    if (!(Test-Path -LiteralPath $Stage)) { return }
    $resolvedStage = [IO.Path]::GetFullPath($Stage).TrimEnd('\', '/')
    $resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedStage.StartsWith($resolvedStagingRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a release staging directory outside $stagingRoot"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}

if ($Package -in @('SampleIntegrated', 'All')) {
    Invoke-DotNet @('restore', $headlessProject, '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
    Invoke-DotNet @('build', $headlessProject, '-c', 'Release', '--no-restore', '-m:1', '-p:UseSharedCompilation=false')
}

if ($Package -in @('Editor', 'All')) { Publish-Editor }
if ($Package -in @('Sample', 'SampleIntegrated', 'All')) {
    Publish-SamplePackages -IncludePure:($Package -in @('Sample', 'All')) -IncludeIntegrated:($Package -in @('SampleIntegrated', 'All'))
}

Write-Output "Release packages are in $versionRoot"
