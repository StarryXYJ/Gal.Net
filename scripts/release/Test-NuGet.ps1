[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$script:logRoot = Join-Path $WorkDirectory 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$mapping = Get-Content (Join-Path $repoRoot 'packaging/packages.json') -Raw | ConvertFrom-Json -AsHashtable
$owned = @{}
foreach ($packageId in $mapping.Keys) {
    $path = Join-Path $PackageDirectory "$packageId.$Version.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $specEntry = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
        if ($specEntry.Count -ne 1) { throw "Invalid nuspec count in $path" }
        $reader = [IO.StreamReader]::new($specEntry[0].Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($dependency in $nuspec.SelectNodes('//*[local-name()="dependency"]')) {
            if ($dependency.id.StartsWith('GalNet.') -and !$mapping.Contains($dependency.id)) { throw "Unpublished dependency $($dependency.id) in $path" }
        }
        $dlls = @($archive.Entries | Where-Object { $_.FullName -match '^lib/.*\.dll$|^analyzers/.*\.dll$' })
        foreach ($dll in $dlls) {
            $name = [IO.Path]::GetFileNameWithoutExtension($dll.FullName)
            if ($owned.ContainsKey($name)) { throw "Duplicate DLL $name" }
            $owned[$name] = $packageId
        }
        foreach ($project in $mapping[$packageId].projects) {
            $name = Split-Path $project -Leaf
            if ($owned[$name] -ne $packageId) { throw "Missing $name in $packageId" }
        }
        if ($packageId -eq 'GalNet.Desktop' -and !$archive.GetEntry('runtimes/win-x64/native/galnet_miniaudio.dll')) { throw 'Missing native audio asset.' }
    }
    finally { $archive.Dispose() }
}

# Outside the checkout: no Directory.Build.props, project references or central versions can hide a bad package.
$consumerRoot = Join-Path ([IO.Path]::GetTempPath()) "galnet-consumer-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $consumerRoot | Out-Null
$sources = @($PackageDirectory, 'https://api.nuget.org/v3/index.json')
$sourceAssets = Get-Content (Join-Path $repoRoot 'src/Shared/GalNet.Core/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
# Installed package caches also form valid hierarchical offline feeds for third-party packages.
$sources += @($sourceAssets.packageFolders.Keys)
$configuration = [xml]'<configuration><packageSources><clear /></packageSources></configuration>'
$index = 0
foreach ($source in $sources) {
    $node = $configuration.CreateElement('add'); $node.SetAttribute('key', "source$index"); $node.SetAttribute('value', $source)
    [void]$configuration.configuration.packageSources.AppendChild($node); $index++
}
$configuration.Save((Join-Path $consumerRoot 'NuGet.Config'))
$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $consumerRoot 'packages'
try {
    foreach ($packageId in @('GalNet.Contracts', 'GalNet.Engine', 'GalNet.Desktop', 'GalNet.Avalonia')) {
        $consumer = Join-Path $consumerRoot $packageId
        New-Item -ItemType Directory -Path $consumer | Out-Null
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$packageId" Version="[$Version]" />
    <PackageReference Include="GalNet.SchemaGenerator" Version="[$Version]" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $consumer 'Consumer.csproj') -Encoding utf8
        @'
using GalNet.Core.Parameters;
Console.WriteLine(ConsumerOptions.Schema is not null ? "PACKAGE_CONSUMER_OK" : throw new Exception("Generator missing"));
foreach (var library in Directory.EnumerateFiles(AppContext.BaseDirectory, "GalNet*.dll"))
{
    if (Path.GetFileName(library) == "GalNet.SchemaGenerator.dll") throw new Exception("Analyzer leaked into runtime output");
    _ = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(library).GetExportedTypes();
}
var native = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "galnet_miniaudio.dll");
if (File.Exists(native))
{
    var handle = System.Runtime.InteropServices.NativeLibrary.Load(native);
    try { _ = System.Runtime.InteropServices.NativeLibrary.GetExport(handle, "gn_abi_version"); }
    finally { System.Runtime.InteropServices.NativeLibrary.Free(handle); }
}
[GenerateFieldSchema]
public partial class ConsumerOptions
{
    [Field(0)]
    public string Name { get; set; } = "consumer";
}
'@ | Set-Content (Join-Path $consumer 'Program.cs') -Encoding utf8
        $project = Join-Path $consumer 'Consumer.csproj'
        Invoke-ReleaseCommand "restore-$packageId" 'dotnet' @('restore', $project, '--configfile', (Join-Path $consumerRoot 'NuGet.Config'), '--ignore-failed-sources', '-p:NuGetAudit=false')
        Invoke-ReleaseCommand "build-$packageId" 'dotnet' @('build', $project, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false')
        Invoke-ReleaseCommand "run-$packageId" 'dotnet' @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore')
        $assets = Get-Content (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
        $names = @($assets.libraries.Keys | ForEach-Object { $_.Split('/')[0] })
        if ($packageId -eq 'GalNet.Contracts' -and ($names -contains 'GalNet.Engine' -or $names -contains 'CoreCLR-NCalc')) { throw 'Contracts leaked runtime dependencies.' }
        if ($packageId -eq 'GalNet.Engine' -and ($names -contains 'GalNet.Desktop' -or $names -contains 'Avalonia' -or $names -contains 'LibVLCSharp')) { throw 'Engine leaked backend dependencies.' }
        if ($packageId -in @('GalNet.Desktop', 'GalNet.Avalonia')) {
            if (!(Test-Path (Join-Path $consumer 'bin/Release/net10.0/runtimes/win-x64/native/galnet_miniaudio.dll'))) { throw "Native audio did not flow to $packageId consumer." }
            $sdl = Join-Path $consumer 'bin/Release/net10.0/runtimes/win-x64/native/SDL3.dll'
            if (!(Test-Path -LiteralPath $sdl)) { throw "Native SDL did not flow to $packageId consumer." }
            $notices = Join-Path $consumer 'bin/Release/net10.0/ThirdParty/SDL3'
            foreach ($notice in @('SDL-LICENSE.txt', 'SDL3-CS-LICENSE.txt', 'native-runtime.json')) {
                if (!(Test-Path -LiteralPath (Join-Path $notices $notice))) { throw "SDL notice missing from $packageId consumer: $notice" }
            }
            $inventory = Get-Content (Join-Path $notices 'native-runtime.json') -Raw | ConvertFrom-Json
            if ((Get-FileHash -LiteralPath $sdl -Algorithm SHA256).Hash.ToLowerInvariant() -ne $inventory.assets.'win-x64'.sha256) {
                throw "SDL native inventory mismatch in $packageId consumer."
            }
        }
    }
    [ordered]@{ passed = $true; consumerDirectory = $consumerRoot; packageCount = $mapping.Count; version = $Version } | ConvertTo-Json | Set-Content (Join-Path $WorkDirectory 'package-validation.json') -Encoding utf8
}
finally { $env:NUGET_PACKAGES = $previousPackages }
Write-Host "External NuGet consumers verified: $consumerRoot"
