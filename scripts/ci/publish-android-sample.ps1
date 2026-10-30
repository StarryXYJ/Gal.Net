[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('android-arm64')][string]$RuntimeIdentifier = 'android-arm64',
    [string]$AndroidNdkDirectory,
    [string]$CMakePath = 'cmake',
    [string]$NinjaPath = 'ninja',
    [string]$Version = '0.0.0-dev.1'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Use a new Android publish output directory: $OutputDirectory" }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-(?:dev|preview)\.\d+)?$') { throw 'Use a release or numbered dev/preview version.' }
if ([string]::IsNullOrWhiteSpace($AndroidNdkDirectory)) { $AndroidNdkDirectory = $env:ANDROID_NDK_ROOT }
if ([string]::IsNullOrWhiteSpace($AndroidNdkDirectory) -or !(Test-Path -LiteralPath (Join-Path $AndroidNdkDirectory 'build/cmake/android.toolchain.cmake'))) {
    throw 'Android NDK r28+ is required. Pass -AndroidNdkDirectory or set ANDROID_NDK_ROOT.'
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$logs = Join-Path $OutputDirectory 'logs'
New-Item -ItemType Directory -Path $logs | Out-Null
$steps = [Collections.Generic.List[object]]::new()

function Invoke-AndroidDotNet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $logs "$Name.log"
    & dotnet @Arguments *> $log
    $exitCode = $LASTEXITCODE
    $steps.Add([ordered]@{ name = $Name; command = "dotnet $($Arguments -join ' ')"; exitCode = $exitCode; log = $log })
    if ($exitCode -ne 0) {
        $tail = Get-Content -LiteralPath $log -Tail 80 | Out-String
        throw "Android step '$Name' failed with exit code $exitCode. Log: $log`n$tail"
    }
}

function Test-AndroidContentContracts([string]$HeadlessProject, [string]$ContentRoot, [string]$AndroidProject, [string]$Rid) {
    $inspectionLog = Join-Path $logs 'inspect-gametestcase.log'
    & dotnet run --project $HeadlessProject --configuration Release --no-launch-profile --no-build --no-restore -- game inspect $ContentRoot *> $inspectionLog
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect exported GameTestCase content. Log: $inspectionLog" }
    $inspection = Get-Content -LiteralPath $inspectionLog -Raw | ConvertFrom-Json
    if (!$inspection.success -or !$inspection.data.contractFiles) { throw 'GameTestCase export did not report its contract closure.' }
    $androidProjectDirectory = Split-Path -Parent $AndroidProject
    $contractRoots = @(
        (Join-Path $androidProjectDirectory "bin/Release/net10.0-android/$Rid"),
        (Join-Path $androidProjectDirectory "obj/Release/net10.0-android/$Rid/linked"),
        (Join-Path $androidProjectDirectory "obj/Release/net10.0-android/$Rid/linked/shrunk")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    if (!$contractRoots.Count) { throw 'Android publish did not retain a managed contract input directory.' }
    $verified = @()
    foreach ($entry in $inspection.data.contractFiles) {
        $name = [IO.Path]::GetFileName($entry.path)
        if ($name -ne $entry.path) { throw "Invalid contract path in exported GameTestCase: $($entry.path)" }
        foreach ($root in $contractRoots) {
            $candidate = Join-Path $root $name
            if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Android runtime contract is missing: $candidate" }
            $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -cne $entry.sha256 -or (Get-Item -LiteralPath $candidate).Length -ne $entry.size) {
                throw "Android runtime contract differs from GameTestCase: $name in $root"
            }
            $verified += [ordered]@{ path = "$([IO.Path]::GetRelativePath($repoRoot, $candidate).Replace('\', '/'))"; sha256 = $hash; size = (Get-Item -LiteralPath $candidate).Length }
        }
    }
    return [ordered]@{ content = $ContentRoot; inspectionLog = $inspectionLog; contracts = $verified }
}

try {
    $sdk = (& dotnet --version).Trim()
    $expectedSdk = (Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($sdk -ne $expectedSdk) { throw "SDK must be exactly $expectedSdk; got $sdk." }

    $headless = Join-Path $repoRoot 'src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj'
    $android = Join-Path $repoRoot 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Android/GalNet.Sample.CrossPlatform.Android.csproj'
    $contentOutput = Join-Path $OutputDirectory 'content'
    $properties = @(
        '-p:Configuration=Release',
        "-p:RuntimeIdentifier=$RuntimeIdentifier",
        "-p:RuntimeIdentifiers=$RuntimeIdentifier",
        '-p:UseMonoRuntime=true',
        '-p:PublishAot=false',
        '-p:RunAOTCompilation=true',
        '-p:AndroidEnableProfiledAot=false',
        '-p:EnableGameDebugging=false',
        '-p:EnableLaunchArguments=false',
        '-p:CopyOutputSymbolsToPublishDirectory=false',
        '-p:GalNetBuildMiniaudio=false',
        "-p:GalNetAndroidNdkDirectory=$AndroidNdkDirectory",
        "-p:GalNetCMakePath=$CMakePath",
        "-p:GalNetNinjaPath=$NinjaPath",
        "-p:Version=$Version",
        '-p:ContinuousIntegrationBuild=true',
        '-p:NuGetAudit=false',
        '-p:UseSharedCompilation=false',
        '-m:1',
        '-nr:false'
    )

    Invoke-AndroidDotNet 'restore-headless' @('restore', $headless, '--locked-mode', '-p:Configuration=Release', '-p:NuGetAudit=false', '-m:1', '-nr:false')
    Invoke-AndroidDotNet 'build-headless' @('build', $headless, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false', '-m:1', '-nr:false')
    Invoke-AndroidDotNet 'export-gametestcase' @('run', '--project', $headless, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore',
        '--', 'project', 'export', (Join-Path $repoRoot 'GameTestCase'), '--output', $contentOutput)
    $galpak = Join-Path $contentOutput 'GameTestCase.galpak'
    if (!(Test-Path -LiteralPath $galpak -PathType Leaf)) { throw "GameTestCase export did not produce $galpak." }
    $installedContent = Join-Path $OutputDirectory 'installed-content'
    Invoke-AndroidDotNet 'install-gametestcase' @('run', '--project', $headless, '--configuration', 'Release', '--no-launch-profile', '--no-build', '--no-restore',
        '--', 'galpak', 'install', $galpak, '--output', $installedContent)

    $publishProperties = @($properties) + @("-p:GalNetGamePackage=$galpak")
    Invoke-AndroidDotNet 'restore-android' (@('restore', $android) + $publishProperties + @('--locked-mode'))
    $publish = Join-Path $OutputDirectory 'publish'
    Invoke-AndroidDotNet 'publish-android' (@('publish', $android, '-c', 'Release', '--no-restore', '-o', $publish) + $publishProperties)
    $contractVerification = Test-AndroidContentContracts $headless $installedContent $android $RuntimeIdentifier
    $apks = @(Get-ChildItem -LiteralPath $publish -Filter '*-Signed.apk' -File -Recurse)
    if ($apks.Count -ne 1) { throw "Expected exactly one signed Android APK, found $($apks.Count) under $publish." }

    $verification = Join-Path $OutputDirectory 'android-package-verification.json'
    & python (Join-Path $repoRoot 'features/F-20261029-05-cross-platform-applications/probes/mobile/verify-android-package.py') $apks[0].FullName $galpak '--abi' 'arm64-v8a' '--output' $verification
    if ($LASTEXITCODE -ne 0) { throw "Android package verification failed: $verification" }
    [ordered]@{
        passed = $true
        preview = $true
        runtimeIdentifier = $RuntimeIdentifier
        gameDebugging = $false
        gameTestCaseBundled = $true
        monoAot = $true
        selfContained = $true
        publicDotnetRuntimeRequired = $false
        apk = $apks[0].FullName
        content = $galpak
        contractVerification = $contractVerification
        packageVerification = $verification
        sdkVersion = $sdk
        steps = @($steps)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'android-publish.json') -Encoding utf8
}
catch {
    [ordered]@{
        passed = $false
        preview = $true
        runtimeIdentifier = $RuntimeIdentifier
        error = $_.Exception.Message
        steps = @($steps)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'android-publish.json') -Encoding utf8
    throw
}
