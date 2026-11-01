param(
    [Parameter(Mandatory)] [string] $Consumer,
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $Output
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$consumerRoot = (Resolve-Path -LiteralPath $Consumer).Path
$evidence = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $evidence) { throw 'Use a new output directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$projectRoot = Join-Path $consumerRoot 'NativeFlow'
New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeFlow.cs') -Destination (Join-Path $projectRoot 'Program.cs')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.csproj"/>
    <PackageReference Include="Avalonia.Desktop"/>
    <PackageReference Include="VideoLAN.LibVLC.Windows" Condition="$([MSBuild]::IsOSPlatform('Windows'))"/>
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'NativeFlow.csproj') -Encoding utf8
& dotnet build (Join-Path $projectRoot 'NativeFlow.csproj') -c Release *> (Join-Path $evidence 'build.log')
if ($LASTEXITCODE) { throw 'Native consumer build failed.' }
$binary = Join-Path $projectRoot 'bin/Release/net10.0'
$author = Join-Path $evidence 'author/TemplateNativeFlow'
New-Item -ItemType Directory -Path $author -Force | Out-Null
foreach ($name in @('Assets', 'Graph', 'I18n', 'settings.json')) {
    Copy-Item -LiteralPath (Join-Path $repository "GameTestCase/$name") -Destination $author -Recurse
}
$contracts = Join-Path $author 'Contracts'
New-Item -ItemType Directory -Path $contracts | Out-Null
foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
    Copy-Item -LiteralPath (Join-Path $binary $name) -Destination $contracts
}
$content = Join-Path $evidence 'content'
& $Cli project export-directory $author --output $content *> (Join-Path $evidence 'export.log')
if ($LASTEXITCODE) { throw 'Content export failed.' }
& dotnet (Join-Path $binary 'NativeFlow.dll') $content (Join-Path $evidence 'window') *> (Join-Path $evidence 'window.log')
if ($LASTEXITCODE) { throw 'Native window flow failed.' }
$report = Get-Content -LiteralPath (Join-Path $evidence 'window/native-flow.json') -Raw | ConvertFrom-Json
if (!$report.passed -or $report.headless) { throw 'Native window evidence did not pass.' }
