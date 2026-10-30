[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$MiniaudioNativePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Player', 'Developer')][string]$SampleFlavor = 'Developer'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Version = Get-ReleaseVersion $Version
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$MiniaudioNativePath = (Resolve-Path -LiteralPath $MiniaudioNativePath).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw "Use a new raw output directory: $OutputDirectory" }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$script:logRoot = Join-Path $OutputDirectory 'logs'
New-Item -ItemType Directory -Path $logRoot | Out-Null
$projects = [ordered]@{
    Sample = 'src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj'
    Editor = 'src/Editor/GalNet.Editor/GalNet.Editor.csproj'
    CLI = 'src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj'
}
foreach ($name in $projects.Keys) {
    $arguments = @('publish', (Join-Path $repoRoot $projects[$name]), '-c', 'Release', '-r', 'linux-x64',
        '-p:PublishProfile=Desktop', '-p:GalNetDesktopPublishTarget=linux-x64',
        "-p:GalNetMiniaudioNativePath=$MiniaudioNativePath", "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true',
        '-p:RestoreLockedMode=true', '-p:NuGetAudit=false', '-p:UseSharedCompilation=false', '-m:1', '-nr:false',
        '-o', (Join-Path $OutputDirectory $name))
    if ($name -eq 'Sample') {
        $debugging = ($SampleFlavor -eq 'Developer').ToString().ToLowerInvariant()
        $arguments += @("-p:EnableGameDebugging=$debugging", "-p:EnableLaunchArguments=$debugging")
    }
    Invoke-ReleaseCommand "linux-publish-$name" 'dotnet' $arguments
}
[ordered]@{ version = $Version; runtime = 'linux-x64'; sampleFlavor = $SampleFlavor; readyToRun = $false } |
    ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'publish.json') -Encoding utf8
Write-Host "Raw Linux outputs: $OutputDirectory. Assemble and verify with package-linux.py in Ubuntu 24.04."
