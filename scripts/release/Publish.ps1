[CmdletBinding()]
param(
    [ValidateSet('Editor', 'Sample', 'SampleIntegrated', 'All')]
    [string]$Package = 'All',
    [string]$Version,
    [string]$OutputRoot,
    [string]$ProjectDirectory,
    [string]$Galpak,
    [string]$ContractOutputDirectory,
    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$Deployment = 'SelfContained',
    [ValidateSet('Player', 'Developer')]
    [string]$SampleFlavor = 'Player',
    [switch]$IncludeModeInName
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$headlessProject = Join-Path $repoRoot 'src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj'
$selfContained = $Deployment -eq 'SelfContained'
$deploymentProperty = "-p:SelfContained=$($selfContained.ToString().ToLowerInvariant())"
$sampleDebugging = ($SampleFlavor -eq 'Developer').ToString().ToLowerInvariant()

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

function Invoke-DotNet([string[]]$Arguments, [switch]$JsonOutput) {
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
    if ($JsonOutput) { return Get-Content -LiteralPath $logPath -Raw | ConvertFrom-Json }
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
        if ($ExcludedRootFiles -contains $relative.Replace('\', '/')) { continue }
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
        selfContained = $selfContained
        readyToRun = $true
        sourceCommit = $commit
        sampleFlavor = if ($Kind.StartsWith('Sample')) { $SampleFlavor } else { $null }
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PackageRoot 'docs/release-manifest.json') -Encoding utf8
}

function Set-PackageLayout([string]$PackageRoot) {
    $rootPath = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $libraryRoot = Join-Path $PackageRoot 'lib'
    $documentsRoot = Join-Path $PackageRoot 'docs'
    New-Item -ItemType Directory -Force -Path $libraryRoot, $documentsRoot | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $PackageRoot -Force) {
        if ($item.Name -in @('lib', 'docs', 'pak', 'Skills', 'Tools', 'GalNet.Editor.exe', 'GalNet.Editor.Headless.exe', 'GalNet.Sample.Avalonia.exe')) { continue }
        $destination = Join-Path $(if ($item.Name -in @('README.txt', 'README.md', 'LICENSE', 'ThirdParty')) { $documentsRoot } else { $libraryRoot }) $item.Name
        if (!$item.FullName.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase) -or
            ![IO.Path]::GetFullPath($destination).StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to move files outside the application payload.'
        }
        Move-Item -LiteralPath $item.FullName -Destination $destination -Force
    }
}

function Complete-Package([string]$Kind, [string]$PackageRoot) {
    # Some native NuGet content ignores the publish RID and symbol-copy setting.
    # Filter the final payload, including the nested CLI, before hashing it.
    $payloadRoot = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $unwanted = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File | Where-Object {
        ($_.Extension -eq '.pdb' -and !($Kind.StartsWith('Sample') -and $SampleFlavor -eq 'Developer')) -or
        (Get-RelativePath $PackageRoot $_.FullName).Replace('\', '/') -match '(^|/)libvlc/win-(x86|arm64)/'
    })
    foreach ($file in $unwanted) {
        if (!$file.FullName.StartsWith($payloadRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a file outside the release payload: $($file.FullName)"
        }
        Remove-Item -LiteralPath $file.FullName -Force
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $PackageRoot 'LICENSE') -Force
    Set-PackageLayout $PackageRoot
    if ($Kind -eq 'Editor') {
        $composeTools = Join-Path $PackageRoot 'Tools'
        New-Item -ItemType Directory -Path $composeTools -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/distribution/compose.ps1') -Destination $composeTools
        Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/distribution/compose.sh') -Destination $composeTools
        Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/distribution/build-compose.ps1') -Destination $composeTools
        Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/distribution/build-compose.sh') -Destination $composeTools
    }
    $applicationLayout = [ordered]@{
        schemaVersion = 1
        runtimeIdentifier = 'win-x64'
        entryPoint = if ($Kind -eq 'Editor') { 'GalNet.Editor.exe' } else { 'GalNet.Sample.Avalonia.exe' }
        managedDirectory = 'lib'
        contractsDirectory = if ($Kind -eq 'Editor') { 'lib/DefaultContracts' } else { 'lib' }
        contentDirectory = 'pak'
    }
    $applicationLayout | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PackageRoot 'docs/application-layout.json') -Encoding utf8
    if (!$selfContained) {
        @'

Requires the Windows x64 .NET 10 Runtime (latest servicing patch).
Install the .NET Runtime, not the SDK; this Avalonia app does not require
the Windows Desktop Runtime or ASP.NET Core Runtime.
Download: https://dotnet.microsoft.com/download/dotnet/10.0
'@ | Add-Content -LiteralPath (Join-Path $PackageRoot 'docs/README.txt') -Encoding utf8
    }
    Add-PackageManifest $PackageRoot $Kind
    $modeSuffix = if ($IncludeModeInName) { '-r2r' } else { '' }
    if (!$selfContained) { $modeSuffix += '-framework-dependent' }
    if ($Kind.StartsWith('Sample') -and $SampleFlavor -eq 'Developer') { $modeSuffix += '-developer' }
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
        Invoke-DotNet @('restore', $project, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, "-p:PackageAppHostDestinationPath=$hostRoot/GalNet.Editor.exe", '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', $hostRoot)
        $cliRoot = Join-Path $stage 'cli'
        Invoke-DotNet @('restore', $headlessProject, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $headlessProject, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, "-p:PackageAppHostDestinationPath=$cliRoot/GalNet.Editor.Headless.exe", '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', $cliRoot)
        # Both apphosts target lib/<app>.dll. Merge only
        # byte-identical shared files; never silently replace an Editor dependency.
        foreach ($file in Get-ChildItem -LiteralPath $cliRoot -Recurse -File) {
            if ($file.Extension -eq '.pdb' -or $file.Name -eq 'README.md') { continue }
            $destination = Join-Path $hostRoot (Get-RelativePath $cliRoot $file.FullName)
            if (Test-Path -LiteralPath $destination) {
                if ((Get-FileHash $destination).Hash -ne (Get-FileHash $file.FullName).Hash) {
                    throw "Editor/CLI dependency collision: $destination"
                }
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination
            }
        }
        @'
GalNet Editor

Run GalNet.Editor.exe to start the editor.
Run GalNet.Editor.Headless.exe help to use the CLI. Keep both executables
in this directory; they share dependencies and (in the full package) .NET.
'@ | Set-Content -LiteralPath (Join-Path $hostRoot 'README.txt') -Encoding utf8
        Complete-Package 'Editor' $hostRoot
    }
    finally { Remove-Stage $stage }
}

function Add-GameContent([string]$Stage, [string]$PackageRoot, [string]$ContractsRoot) {
    $contentBuildRoot = Join-Path $Stage 'GameTestCase'
    $contentRoot = Join-Path $PackageRoot 'pak'
    if (![string]::IsNullOrWhiteSpace($Galpak)) {
        Invoke-DotNet @('run', '--project', $headlessProject, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore', '--', 'galpak', 'install', $Galpak, '--output', $contentRoot)
        $inspection = Invoke-DotNet @('run', '--project', $headlessProject, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore', '--', 'game', 'inspect', $contentRoot) -JsonOutput
        $buildId = $inspection.data.buildId
        if ($buildId -cnotmatch '^[a-f0-9]{64}$') { throw 'Integrated content has an invalid buildId.' }
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
        if (Test-Path (Join-Path $ContractsRoot 'lib/GalNet.Core.dll')) { $ContractsRoot = Join-Path $ContractsRoot 'lib' }
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
        Invoke-DotNet @('restore', $project, '-r', 'win-x64', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, "-p:EnableGameDebugging=$sampleDebugging", '--locked-mode', '-p:NuGetAudit=false', '-m:1', '-nr:false')
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-p:PublishProfile=WindowsReadyToRun', $deploymentProperty, "-p:EnableGameDebugging=$sampleDebugging", "-p:EnableLaunchArguments=$sampleDebugging", "-p:CopyOutputSymbolsToPublishDirectory=$sampleDebugging", "-p:PackageAppHostDestinationPath=$runtimeRoot/GalNet.Sample.Avalonia.exe", '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '-o', $runtimeRoot)

        if ($ContractOutputDirectory) {
            New-Item -ItemType Directory -Force -Path $ContractOutputDirectory | Out-Null
            foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
                Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $ContractOutputDirectory -Force
            }
        }

        if ($IncludePure) {
            foreach ($directory in @('StoryPatches', 'ResourcePatches', 'I18n')) {
                New-Item -ItemType Directory -Force -Path (Join-Path $runtimeRoot "pak/$directory") | Out-Null
            }
            @'
GalNet Sample

Place the expanded game content in pak/ beside GalNet.Sample.Avalonia.exe,
then double-click the executable. New .galpak archives already contain pak/;
extract them into the application directory. Keep lib/ intact.
Player data and logs are stored under the user's local application data,
in GalNet/<project identity digest>/, independently of the installation path.
The developer flavor also supports content, --profile and --debug-* arguments.
'@ | Set-Content -LiteralPath (Join-Path $runtimeRoot 'README.txt') -Encoding utf8
            Complete-Package 'Sample' $runtimeRoot
        }

        if ($IncludeIntegrated) {
            $packageRoot = Join-Path $stage 'integrated-payload'
            New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
            $excluded = if ($IncludePure) { @('docs/README.txt', 'docs/release-manifest.json') } else { @() }
            Copy-PublishedFiles $runtimeRoot $packageRoot $excluded
            Add-GameContent $stage $packageRoot $runtimeRoot
            @'
GalNet Sample with compiled GameTestCase content

Double-click GalNet.Sample.Avalonia.exe. Compiled content is in pak/;
resources and story data are stored in pak/Assets/Paks/*.pak, with optional
story packages in pak/StoryPatches/. Keep lib/ and pak/ beside the executable.
Player data and logs are stored in the user's local application data.
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
