param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installations = @(& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
if (!$installations) { throw 'The Windows audio backend requires MSVC C++ build tools.' }
# Side-by-side installations can have ARM64 tools only in an older toolset.
$candidates = foreach ($installationRoot in $installations) {
    $toolsRoot = Join-Path $installationRoot 'VC/Tools/MSVC'
    foreach ($tools in Get-ChildItem -LiteralPath $toolsRoot -Directory -ErrorAction SilentlyContinue) {
        if ($tools.Name -notmatch '^\d+(\.\d+){1,3}$') { continue }
        $compiler = Join-Path $tools.FullName "bin/Hostx64/$Architecture/cl.exe"
        $runtime = Join-Path $tools.FullName "lib/$Architecture/libcmt.lib"
        $header = Join-Path $tools.FullName 'include/vcruntime.h'
        if ((Test-Path -LiteralPath $compiler) -and (Test-Path -LiteralPath $runtime) -and (Test-Path -LiteralPath $header)) {
            [pscustomobject]@{ Installation = $installationRoot; Version = [version]$tools.Name }
        }
    }
}
$toolset = $candidates | Sort-Object Version -Descending | Select-Object -First 1
if (!$toolset) { throw "No complete MSVC $Architecture toolset was found. Install the matching C++ build tools, including static runtime libraries." }
$vcvars = Join-Path $toolset.Installation 'VC/Auxiliary/Build/vcvarsall.bat'
$toolsetVersion = $toolset.Version.ToString()
Write-Host "Building miniaudio for Windows $Architecture with MSVC $toolsetVersion."
$target = if ($Architecture -eq 'arm64') { 'x64_arm64' } else { 'x64' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source = Join-Path $PSScriptRoot 'galnet_miniaudio.c'
$dll = Join-Path $OutputDirectory 'galnet_miniaudio.dll'
$obj = Join-Path $OutputDirectory 'galnet_miniaudio.obj'
$lib = Join-Path $OutputDirectory 'galnet_miniaudio.lib'
$job = Join-Path $OutputDirectory 'build.cmd'
@"
@echo off
call "$vcvars" $target -vcvars_ver=$toolsetVersion >nul
if errorlevel 1 exit /b %errorlevel%
if not exist "%VCToolsInstallDir%lib\$Architecture\libcmt.lib" (
  echo Missing MSVC $Architecture static runtime libraries. Install the matching C++ build tools. 1>&2
  exit /b 1
)
cl /nologo /O2 /MT /DMA_NO_NULL /LD "$source" /Fo"$obj" /link /OUT:"$dll" /IMPLIB:"$lib"
"@ | Set-Content -LiteralPath $job -Encoding ASCII
& $env:ComSpec /d /c "call `"$job`""
if ($LASTEXITCODE -ne 0) { throw "miniaudio native build failed: $LASTEXITCODE" }
