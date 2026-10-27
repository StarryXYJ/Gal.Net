param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'The Windows audio backend requires MSVC C++ build tools.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
$target = if ($Architecture -eq 'arm64') { 'x64_arm64' } else { 'x64' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source = Join-Path $PSScriptRoot 'galnet_miniaudio.c'
$dll = Join-Path $OutputDirectory 'galnet_miniaudio.dll'
$obj = Join-Path $OutputDirectory 'galnet_miniaudio.obj'
$lib = Join-Path $OutputDirectory 'galnet_miniaudio.lib'
$job = Join-Path $OutputDirectory 'build.cmd'
@"
@echo off
call "$vcvars" $target >nul
if errorlevel 1 exit /b %errorlevel%
if not exist "%VCToolsInstallDir%lib\$Architecture\libcmt.lib" (
  echo Missing MSVC $Architecture static runtime libraries. Install the matching C++ build tools. 1>&2
  exit /b 1
)
cl /nologo /O2 /MT /DMA_NO_NULL /LD "$source" /Fo"$obj" /link /OUT:"$dll" /IMPLIB:"$lib"
"@ | Set-Content -LiteralPath $job -Encoding ASCII
& $env:ComSpec /d /c "call `"$job`""
if ($LASTEXITCODE -ne 0) { throw "miniaudio native build failed: $LASTEXITCODE" }
