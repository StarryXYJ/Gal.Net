[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [switch]$SkipBuild
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Version = Get-ReleaseVersion $Version ''
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$script:logRoot = Join-Path $WorkDirectory 'logs'
New-Item -ItemType Directory -Force -Path $OutputDirectory, $logRoot | Out-Null
$mapping = Get-Content (Join-Path $repoRoot 'packaging/packages.json') -Raw | ConvertFrom-Json -AsHashtable
$commit = & git -C $repoRoot rev-parse HEAD
$buildProperties = @("-p:Version=$Version", '-p:ContinuousIntegrationBuild=true', '-p:UseSharedCompilation=false', '-m:1', '-nr:false')
if (!$SkipBuild) {
    . (Join-Path $PSScriptRoot '../ci/Common.ps1')
    $libraryProjects = @($mapping.Values | ForEach-Object { $_.projects } | ForEach-Object { "src/$_/$(Split-Path $_ -Leaf).csproj" })
    $solution = New-DesktopSolutionFilter -Path (Join-Path $WorkDirectory 'libraries.slnf') -Projects $libraryProjects
    Invoke-ReleaseCommand 'restore-libraries' 'dotnet' (@('restore', $solution, '--locked-mode', '-p:Configuration=Release') + $buildProperties)
    Invoke-ReleaseCommand 'build-libraries' 'dotnet' (@('build', $solution, '-c', 'Release', '--no-restore') + $buildProperties)
}

# All project ownership and dependency mapping is explicit. Each DLL belongs to one package.
$owners = @{}
foreach ($packageId in $mapping.Keys) {
    foreach ($project in $mapping[$packageId].projects) {
        $name = Split-Path $project -Leaf
        if ($owners.ContainsKey($name)) { throw "Duplicate package owner for $name" }
        $owners[$name] = $packageId
    }
}

foreach ($packageId in $mapping.Keys) {
    $spec = $mapping[$packageId]
    $isGenerator = $packageId -eq 'GalNet.SchemaGenerator'
    $framework = if ($isGenerator) { 'netstandard2.0' } else { 'net10.0' }
    $payload = Join-Path $WorkDirectory $packageId
    if (Test-Path -LiteralPath $payload) { throw "Use a fresh WorkDirectory: $payload" }
    New-Item -ItemType Directory -Path $payload | Out-Null
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $payload 'LICENSE')
    $spec.description | Set-Content (Join-Path $payload 'README.md') -Encoding utf8
    $dependencies = [ordered]@{}
    foreach ($dependency in $spec.dependencies) { $dependencies[$dependency] = @{ version = "[$Version]"; exclude = 'Build,Analyzers' } }
    foreach ($project in $spec.projects) {
        $name = Split-Path $project -Leaf
        $projectRoot = Join-Path $repoRoot "src/$project"
        $dllRoot = Join-Path $projectRoot "bin/Release/$framework"
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $dllRoot "$name.dll")).Version.ToString()
        $expectedVersion = $Version.Split('-')[0] + '.0'
        if ($assemblyVersion -ne $expectedVersion) { throw "Stale assembly $name ($assemblyVersion); expected $expectedVersion. Build this release version before packing." }
        $destination = Join-Path $payload $(if ($isGenerator) { 'analyzers/dotnet/cs' } else { "lib/$framework" })
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item (Join-Path $dllRoot "$name.dll") $destination
        if (Test-Path (Join-Path $dllRoot "$name.xml")) { Copy-Item (Join-Path $dllRoot "$name.xml") $destination }
        $assets = Get-Content (Join-Path $projectRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
        foreach ($reference in $assets.project.restore.frameworks[$framework].projectReferences.Keys) {
            $referenceName = [IO.Path]::GetFileNameWithoutExtension($reference)
            if (!$owners.ContainsKey($referenceName)) { throw "Unmapped project dependency: $name -> $referenceName" }
            # Analyzer ProjectReferences are build-only and do not become runtime dependencies.
            if ($referenceName -eq 'GalNet.SchemaGenerator') { continue }
            $owner = $owners[$referenceName]
            if ($owner -ne $packageId) {
                # A direct package dependency is valid even if another dependency already brings it transitively.
                $dependencies[$owner] = @{ version = "[$Version]"; exclude = 'Build,Analyzers' }
            }
        }
        $packageDependencies = $assets.project.frameworks[$framework].dependencies
        if (!$packageDependencies) { $packageDependencies = @{} }
        foreach ($entry in $packageDependencies.GetEnumerator()) {
            $info = $entry.Value
            if ($info.autoReferenced -or $info.suppressParent -eq 'All') { continue }
            $dependency = @{ version = $info.version; exclude = 'Build,Analyzers' }
            if ($info.include) { $dependency.include = $info.include }
            if ($info.exclude) { $dependency.exclude = $info.exclude }
            if ($dependencies.Contains($entry.Key) -and $dependencies[$entry.Key].version -ne $dependency.version) { throw "Inconsistent dependency $($entry.Key) in $packageId" }
            $dependencies[$entry.Key] = $dependency
        }
    }
    if ($packageId -eq 'GalNet.Desktop') {
        $native = Join-Path $payload 'runtimes/win-x64/native'
        New-Item -ItemType Directory -Force -Path $native | Out-Null
        Copy-Item (Join-Path $repoRoot 'src/Infrastructure/GalNet.Media.Miniaudio/obj/native/win-x64/galnet_miniaudio.dll') $native
    }
    $document = [xml]'<package><metadata/><files><file src="**/*" target="" /></files></package>'
    $metadata = $document.SelectSingleNode('/package/metadata')
    foreach ($entry in ([ordered]@{ id = $packageId; version = $Version; authors = 'GalNet contributors'; description = $spec.description; readme = 'README.md' }).GetEnumerator()) {
        $element = $document.CreateElement($entry.Key); $element.InnerText = $entry.Value
        [void]$metadata.AppendChild($element)
    }
    $license = $document.CreateElement('license'); $license.SetAttribute('type', 'file'); $license.InnerText = 'LICENSE'; [void]$metadata.AppendChild($license)
    $repository = $document.CreateElement('repository'); $repository.SetAttribute('type', 'git'); $repository.SetAttribute('url', 'https://github.com/StarryXYJ/Gal.Net'); $repository.SetAttribute('commit', $commit); [void]$metadata.AppendChild($repository)
    $dependencyNode = $document.CreateElement('dependencies')
    $group = $document.CreateElement('group'); $group.SetAttribute('targetFramework', $framework)
    foreach ($dependencyId in $dependencies.Keys) {
        $node = $document.CreateElement('dependency'); $node.SetAttribute('id', $dependencyId)
        foreach ($key in $dependencies[$dependencyId].Keys) { $node.SetAttribute($key, $dependencies[$dependencyId][$key]) }
        [void]$group.AppendChild($node)
    }
    [void]$dependencyNode.AppendChild($group); [void]$metadata.AppendChild($dependencyNode)
    $nuspec = Join-Path $WorkDirectory "$packageId.nuspec"
    $document.Save($nuspec)
    Invoke-ReleaseCommand "pack-$packageId" 'dotnet' @('pack', (Join-Path $repoRoot 'packaging/Bundle.csproj'), '-c', 'Release', '-o', $OutputDirectory, "-p:NuspecFile=$nuspec", "-p:NuspecBasePath=$payload", "-p:PackageVersion=$Version", '-p:NuGetAudit=false')
}
Write-Host "NuGet packages: $OutputDirectory"
