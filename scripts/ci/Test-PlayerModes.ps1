[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $repo "out/player-modes-$([guid]::NewGuid().ToString('N'))" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a fresh player-mode fixture directory.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$props = [Security.SecurityElement]::Escape((Join-Path $repo 'packaging/GalNet.Player.props'))
$targets = [Security.SecurityElement]::Escape((Join-Path $repo 'packaging/GalNet.Player.targets'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <GalNetPlayerMode Condition="'`$(GalNetPlayerMode)' == ''">Player</GalNetPlayerMode>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
  </PropertyGroup>
  <Import Project="$targets" />
</Project>
"@ | Set-Content (Join-Path $OutputRoot 'Consumer.csproj') -Encoding utf8
@'
using System.Reflection;
var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
var mode = metadata["GalNetPlayerMode"];
#if GALNET_GAME_DEBUG
if (mode != "Developer") throw new Exception("Unexpected debugging support");
#else
if (mode == "Developer") throw new Exception("Missing debugging support");
#endif
#if GALNET_LAUNCH_ARGS
if (mode == "Bound") throw new Exception("Bound player allows external launch arguments");
#else
if (mode != "Bound") throw new Exception("External launch support missing");
#endif
if (mode == "Bound" && metadata["GalNetBoundProjectId"] != "custom-project") throw new Exception("Custom binding was lost");
Console.WriteLine("PLAYER_MODE_OK:" + mode);
'@ | Set-Content (Join-Path $OutputRoot 'Program.cs') -Encoding utf8
$project = Join-Path $OutputRoot 'Consumer.csproj'
$package = Join-Path $OutputRoot 'custom.galpak'
'fixture: only validates build conditions; content identity has separate runtime tests' | Set-Content $package
foreach ($mode in @('Player', 'Developer', 'Bound')) {
    $arguments = @('run', '--project', $project, '-p:NuGetAudit=false', '-p:UseSharedCompilation=false')
    if ($mode -ne 'Player') { $arguments += "-p:GalNetPlayerMode=$mode" }
    if ($mode -eq 'Bound') { $arguments += @("-p:GalNetGamePackage=$package", '-p:GalNetBoundProjectId=custom-project') }
    & dotnet @arguments *> (Join-Path $OutputRoot "$mode.log")
    if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath (Join-Path $OutputRoot "$mode.log") -SimpleMatch "PLAYER_MODE_OK:$mode")) {
        throw "Player-mode fixture failed: $mode. See $OutputRoot."
    }
}
foreach ($arguments in @(
    @('-p:GalNetPlayerMode=Bound'),
    @('-p:GalNetPlayerMode=invalid'),
    @('-p:GalNetPlayerMode=Player', '-p:EnableGameDebugging=true')
)) {
    & dotnet msbuild $project -t:ValidateGalNetPlayerMode @arguments *> (Join-Path $OutputRoot 'negative.log')
    if ($LASTEXITCODE -eq 0) { throw "Invalid player-mode build was accepted: $arguments" }
}
Write-Host 'Player-mode compile fixtures passed (3 executable modes, 3 rejected configurations).'
