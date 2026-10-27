[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [switch]$DesktopSmoke
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$AssetDirectory = [IO.Path]::GetFullPath($AssetDirectory)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$script:logRoot = Join-Path $WorkDirectory 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$roots = @{}
foreach ($kind in @('Editor', 'Sample', 'Sample-GameTestCase', 'Sample-Contracts')) {
    $suffix = if ($kind -eq 'Sample-Contracts') { '' } else { '-win-x64-r2r' }
    $zip = Join-Path $AssetDirectory "GalNet-$kind-$Version$suffix.zip"
    $root = Join-Path $WorkDirectory $kind
    if (Test-Path -LiteralPath $root) { throw "Use a fresh validation directory: $root" }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $root)
    $roots[$kind] = $root
    if (Get-ChildItem -LiteralPath $root -Recurse -Filter '*.pdb') { throw "$kind ZIP contains PDB files." }
    if (Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') -match '(^|/)libvlc/win-(x86|arm64)/'
    }) { throw "$kind ZIP contains VLC assets for another architecture." }
    if ($kind -ne 'Sample-Contracts') {
        foreach ($name in @('libvlc.dll', 'libvlccore.dll', 'plugins/codec/libavcodec_plugin.dll')) {
            if (!(Test-Path (Join-Path $root "libvlc/win-x64/$name"))) { throw "Missing VLC x64 dependency: $kind / $name" }
        }
    }
    $manifest = Get-Content (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne $Version) { throw "Incorrect version in $kind manifest." }
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
        if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escaped payload.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Hash mismatch in $kind : $($file.path)" }
    }
    if ($kind -in @('Sample', 'Sample-GameTestCase')) {
        foreach ($name in @('GalNet.Sample.Avalonia.exe', 'coreclr.dll', 'galnet_miniaudio.dll')) {
            if (!(Test-Path (Join-Path $root $name))) { throw "Missing player dependency $name" }
        }
        $stream = [IO.File]::OpenRead((Join-Path $root 'GalNet.Sample.Avalonia.dll'))
        $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            if ($reader.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size -eq 0) { throw 'Sample DLL is not ReadyToRun.' }
        }
        finally { $reader.Dispose(); $stream.Dispose() }
    }
}
foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
    $hash = (Get-FileHash (Join-Path $roots['Sample-Contracts'] $name) -Algorithm SHA256).Hash
    foreach ($kind in @('Sample', 'Sample-GameTestCase')) {
        if ((Get-FileHash (Join-Path $roots[$kind] $name) -Algorithm SHA256).Hash -cne $hash) { throw "Contract identity mismatch: $kind / $name" }
    }
}
$cli = Join-Path $roots.Editor 'CLI/GalNet.Editor.Headless.exe'
Invoke-ReleaseCommand 'distributed-cli-help' $cli @('help')
if (!(Test-Path (Join-Path $roots.Editor 'CLI/Skills/galnet-project-authoring/SKILL.md'))) { throw 'Distributed CLI is missing its authoring skill.' }
$game = Join-Path $roots['Sample-GameTestCase'] 'GameTestCase'
if (!(Test-Path (Join-Path $game 'Graph/graph.json'))) { throw 'Integrated game content is missing.' }
$paks = @(Get-ChildItem (Join-Path $game 'Assets/Paks') -Filter '*.pak' -File -ErrorAction SilentlyContinue)
if (!$paks.Count -or ($paks | Where-Object Length -eq 0)) { throw 'Integrated game resources must be compiled into nonempty PAK files.' }
if (Get-ChildItem (Join-Path $game 'Assets') -Recurse -File | Where-Object Extension -ne '.pak') { throw 'Integrated game contains loose authoring assets.' }
if (!(Get-ChildItem (Join-Path $game 'Graph/groups') -Filter '*.galgroup' -File)) { throw 'Integrated game has no compiled story groups.' }
if ((Test-Path (Join-Path $game 'Contracts')) -or (Get-ChildItem $game -Recurse -Filter '*.rawgalgroup')) { throw 'Integrated game contains authoring sources.' }
if (Test-Path (Join-Path $roots.Sample 'GameTestCase')) { throw 'Pure player contains integrated game content.' }

if ($DesktopSmoke) {
    $profile = Join-Path $WorkDirectory 'smoke-profile'
    New-Item -ItemType Directory -Path $profile | Out-Null
    @'
{"player.galnet.startup.launchCount":{"Uid":"release-smoke-count","Name":"player.galnet.startup.launchCount","Type":1,"Value":1}}
'@ | Set-Content (Join-Path $profile 'player-variables.json') -Encoding utf8
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $roots.Sample 'GalNet.Sample.Avalonia.exe'))
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.ArgumentList.Add($game); $info.ArgumentList.Add('--profile'); $info.ArgumentList.Add($profile)
    $process = [Diagnostics.Process]::Start($info)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $ready = $false
        while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
            $log = @(Get-ChildItem (Join-Path $profile 'logs') -Filter 'game-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1)
            if ($log -and (Get-Content $log[0].FullName -Raw) -match 'Session initialized\. SaveSlots=') { $ready = $true; break }
            Start-Sleep -Milliseconds 250
        }
        if (!$ready) { throw "Distributed player did not initialize. Profile/logs: $profile" }
    }
    finally {
        # Terminate only the smoke process we created; no window-handle or title guessing.
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
[ordered]@{ passed = $true; version = $Version; desktopStartup = [bool]$DesktopSmoke; extractionDirectory = $WorkDirectory } | ConvertTo-Json | Set-Content (Join-Path $WorkDirectory 'app-validation.json') -Encoding utf8
Write-Host "Release payload verification passed: $WorkDirectory"
