[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [ValidateSet('SelfContained', 'FrameworkDependent')][string[]]$Deployments = @('SelfContained', 'FrameworkDependent'),
    [ValidateSet('Player', 'Developer')][string]$SampleFlavor = 'Player',
    [switch]$DesktopSmoke
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$AssetDirectory = [IO.Path]::GetFullPath($AssetDirectory)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$script:logRoot = Join-Path $WorkDirectory 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
foreach ($deployment in $Deployments) {
    $selfContained = $deployment -eq 'SelfContained'
    $deploymentWork = Join-Path $WorkDirectory $deployment
    $roots = @{}
    foreach ($kind in @('Editor', 'Sample', 'Sample-GameTestCase', 'Sample-Contracts')) {
        $suffix = if ($kind -eq 'Sample-Contracts') { '' } else { '-win-x64-r2r' }
        if ($kind -ne 'Sample-Contracts' -and !$selfContained) { $suffix += '-framework-dependent' }
        if ($kind -in @('Sample', 'Sample-GameTestCase') -and $SampleFlavor -eq 'Developer') { $suffix += '-developer' }
        $zip = Join-Path $AssetDirectory "GalNet-$kind-$Version$suffix.zip"
        $root = Join-Path $deploymentWork $kind
        if (Test-Path -LiteralPath $root) { throw "Use a fresh validation directory: $root" }
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $root)
        $roots[$kind] = $root
        $isDeveloper = $kind -in @('Sample', 'Sample-GameTestCase') -and $SampleFlavor -eq 'Developer'
        if (!$isDeveloper -and (Get-ChildItem -LiteralPath $root -Recurse -Filter '*.pdb')) { throw "$kind ZIP contains PDB files." }
        if ($isDeveloper -and !(Test-Path (Join-Path $root 'lib/GalNet.Sample.Avalonia.pdb'))) { throw 'Developer ZIP is missing player symbols.' }
        if (Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
            [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') -match '(^|/)libvlc/win-(x86|arm64)/'
        }) { throw "$kind ZIP contains VLC assets for another architecture." }
        if ($kind -ne 'Sample-Contracts') {
            foreach ($name in @('libvlc.dll', 'libvlccore.dll', 'plugins/codec/libavcodec_plugin.dll')) {
                if (!(Test-Path (Join-Path $root "lib/libvlc/win-x64/$name"))) { throw "Missing VLC x64 dependency: $kind / $name" }
            }
        }
        $manifestPath = Join-Path $root $(if ($kind -eq 'Sample-Contracts') { 'release-manifest.json' } else { 'docs/release-manifest.json' })
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.version -ne $Version) { throw "Incorrect version in $kind manifest." }
        if ($kind -ne 'Sample-Contracts') {
            if ($manifest.selfContained -ne $selfContained) { throw "Incorrect deployment mode: $kind" }
            $runtimeFiles = @(Get-ChildItem $root -Recurse -Filter coreclr.dll)
            if ($runtimeFiles.Count -ne [int]$selfContained) { throw "Expected $([int]$selfContained) runtime copies: $kind" }
            if ($selfContained -and !(Test-Path (Join-Path $root 'lib/coreclr.dll'))) { throw "Runtime must be in lib: $kind" }
            if (!$selfContained -and (Get-ChildItem $root -Recurse -File | Where-Object Name -in @('hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll'))) { throw "Framework-dependent package contains runtime files: $kind" }
            $apps = if ($kind -eq 'Editor') { @('GalNet.Editor', 'GalNet.Editor.Headless') } else { @('GalNet.Sample.Avalonia') }
            foreach ($app in $apps) {
                if (!(Test-Path (Join-Path $root "$app.exe"))) { throw "Missing apphost: $app" }
                $config = Get-Content (Join-Path $root "lib/$app.runtimeconfig.json") -Raw | ConvertFrom-Json
                if ($selfContained) {
                    if (!$config.runtimeOptions.includedFrameworks) { throw "Missing bundled framework declaration: $app" }
                }
                elseif ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App' -or $config.runtimeOptions.framework.version -notlike '10.*') { throw "Expected .NET 10 runtime dependency: $app" }
                $stream = [IO.File]::OpenRead((Join-Path $root "lib/$app.dll"))
                $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
                try {
                    if ($reader.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size -eq 0) { throw "$app DLL is not ReadyToRun." }
                }
                finally { $reader.Dispose(); $stream.Dispose() }
            }
            $rootFiles = @(Get-ChildItem -LiteralPath $root -File)
            if ($rootFiles.Count -ne $apps.Count -or ($rootFiles | Where-Object Name -notin @($apps | ForEach-Object { "$_.exe" }))) {
                throw "Application root must contain only entry executables and directories: $kind"
            }
            if ($kind -in @('Sample', 'Sample-GameTestCase')) {
                if ($manifest.sampleFlavor -ne $SampleFlavor) { throw 'Sample flavor mismatch.' }
                if ((Test-Path (Join-Path $root 'lib/GalNet.Debugging.dll')) -ne $isDeveloper) { throw 'Incorrect game debugging dependency.' }
            }
        }
        foreach ($file in $manifest.files) {
            $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
            if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escaped payload.' }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Hash mismatch in $kind : $($file.path)" }
        }
        if ($kind -in @('Sample', 'Sample-GameTestCase')) {
            foreach ($name in @('GalNet.Sample.Avalonia.exe', 'lib/galnet_miniaudio.dll')) {
                if (!(Test-Path (Join-Path $root $name))) { throw "Missing player dependency $name" }
            }
        }
    }
    foreach ($name in @('GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll')) {
        $hash = (Get-FileHash (Join-Path $roots['Sample-Contracts'] $name) -Algorithm SHA256).Hash
        foreach ($kind in @('Sample', 'Sample-GameTestCase')) {
            if ((Get-FileHash (Join-Path $roots[$kind] "lib/$name") -Algorithm SHA256).Hash -cne $hash) { throw "Contract identity mismatch: $kind / $name" }
        }
    }
    $cli = Join-Path $roots.Editor 'GalNet.Editor.Headless.exe'
    Invoke-ReleaseCommand "distributed-cli-help-$deployment" $cli @('help')
    if (!(Test-Path (Join-Path $roots.Editor 'Skills/galnet-project-authoring/SKILL.md'))) { throw 'Distributed CLI is missing its authoring skill.' }
    $authorProject = Join-Path $deploymentWork "cli-project-$([guid]::NewGuid().ToString('N'))"
    Invoke-ReleaseCommand "distributed-cli-create-$deployment" $cli @('project', 'create', $authorProject, '--contracts', $roots['Sample-Contracts'])
    Invoke-ReleaseCommand "distributed-cli-schema-$deployment" $cli @('schema', 'list', $authorProject)
    $game = Join-Path $roots['Sample-GameTestCase'] 'pak'
    $content = Get-Content (Join-Path $game 'content.json') -Raw | ConvertFrom-Json
    if ($content.version -ne 2 -or !$content.projectId -or !$content.buildId -or !$content.basePaks.Count) { throw 'Integrated content descriptor is missing or invalid.' }
    $paks = @(Get-ChildItem (Join-Path $game 'Assets/Paks') -Filter '*.pak' -File -ErrorAction SilentlyContinue)
    if (!$paks.Count -or ($paks | Where-Object Length -eq 0)) { throw 'Integrated game resources must be compiled into nonempty PAK files.' }
    if (Get-ChildItem (Join-Path $game 'Assets') -Recurse -File | Where-Object Extension -ne '.pak') { throw 'Integrated game contains loose authoring assets.' }
    if (Test-Path (Join-Path $game 'Graph')) { throw 'Integrated story data must be in PAK resources.' }
    if ((Test-Path (Join-Path $game 'Contracts')) -or (Get-ChildItem $game -Recurse -Filter '*.rawgalgroup')) { throw 'Integrated game contains authoring sources.' }
    if (Test-Path (Join-Path $roots.Sample 'pak')) { throw 'Pure player contains integrated game content.' }

    if ($DesktopSmoke) {
        $editorOutput = Join-Path $deploymentWork 'editor-stdout.log'
        $editorError = Join-Path $deploymentWork 'editor-stderr.log'
        $editor = Start-Process -FilePath (Join-Path $roots.Editor 'GalNet.Editor.exe') -PassThru -WindowStyle Hidden -RedirectStandardOutput $editorOutput -RedirectStandardError $editorError
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            $ready = $false
            while (!$editor.HasExited -and [DateTime]::UtcNow -lt $deadline) {
                if ((Get-Content $editorOutput -Raw -ErrorAction SilentlyContinue) -match '=== GalNet Editor starting ===') {
                    $ready = $true
                    break
                }
                Start-Sleep -Milliseconds 250
            }
            if (!$ready) { throw "Distributed Editor did not initialize: $editorOutput / $editorError" }
            $runtime = @($editor.Modules | Where-Object ModuleName -eq 'coreclr.dll')
            if ($runtime.Count -ne 1) { throw 'Could not identify Editor runtime.' }
            $localRuntime = $runtime[0].FileName.StartsWith($roots.Editor + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
            if ($localRuntime -ne $selfContained) { throw "Editor loaded the wrong runtime: $($runtime[0].FileName)" }
            $runtime[0].FileName | Set-Content (Join-Path $deploymentWork 'editor-runtime.txt')
        }
        finally {
            if (!$editor.HasExited) { $editor.Kill($true); $editor.WaitForExit() }
            $editor.Dispose()
        }
        # Use a unique exported project so fixed-path player builds exercise their
        # real defaults without touching an existing game's user profile.
        $smokeRoot = Join-Path $deploymentWork 'player smoke with spaces'
        New-Item -ItemType Directory -Path $smokeRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $roots.Sample 'lib') -Destination (Join-Path $smokeRoot 'lib') -Recurse
        Copy-Item -LiteralPath (Join-Path $roots.Sample 'GalNet.Sample.Avalonia.exe') -Destination $smokeRoot
        Invoke-ReleaseCommand "distributed-cli-export-$deployment" $cli @('project', 'export-directory', $authorProject, '--output', (Join-Path $smokeRoot 'pak'))
        $smokeContent = Get-Content (Join-Path $smokeRoot 'pak/content.json') -Raw | ConvertFrom-Json
        $projectKey = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("project:$($smokeContent.projectId)")))
        $userDataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GalNet'
        $profile = Join-Path $userDataRoot $projectKey
        if (Test-Path -LiteralPath $profile) { throw "Smoke profile must be new: $profile" }
        New-Item -ItemType Directory -Path $profile | Out-Null
        try {
            @'
{"player.galnet.startup.launchCount":{"Uid":"release-smoke-count","Name":"player.galnet.startup.launchCount","Type":1,"Value":1}}
'@ | Set-Content (Join-Path $profile 'player-variables.json') -Encoding utf8
            $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $smokeRoot 'GalNet.Sample.Avalonia.exe'))
            $info.UseShellExecute = $false; $info.CreateNoWindow = $true
            $info.WorkingDirectory = $deploymentWork
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
                Copy-Item -LiteralPath $log[0].FullName -Destination (Join-Path $deploymentWork 'player-startup.log')
                if (Test-Path (Join-Path $smokeRoot 'pak/.galnet')) { throw 'Player wrote its profile into installed content.' }
            }
            finally {
                # Terminate only the smoke process we created.
                if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
                $process.Dispose()
            }
        }
        finally {
            $resolvedProfile = [IO.Path]::GetFullPath($profile)
            if ([IO.Path]::GetDirectoryName($resolvedProfile) -ne [IO.Path]::GetFullPath($userDataRoot) -or [IO.Path]::GetFileName($resolvedProfile) -cne $projectKey) {
                throw 'Refusing to remove a profile outside the unique smoke target.'
            }
            Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
        }
    }
}
[ordered]@{ passed = $true; version = $Version; sampleFlavor = $SampleFlavor; deployments = $Deployments; desktopStartup = [bool]$DesktopSmoke; extractionDirectory = $WorkDirectory } | ConvertTo-Json | Set-Content (Join-Path $WorkDirectory 'app-validation.json') -Encoding utf8
Write-Host "Release payload verification passed: $WorkDirectory"
