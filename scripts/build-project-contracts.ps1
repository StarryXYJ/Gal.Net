param(
    [Parameter(Mandatory)][string]$ContractsProject,
    [Parameter(Mandatory)][string]$ProjectDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$contractSource = (Resolve-Path -LiteralPath $ContractsProject).Path
$contentRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$destination = Join-Path $contentRoot 'Contracts'
$staging = Join-Path ([IO.Path]::GetTempPath()) ('GalNet-contracts-' + [Guid]::NewGuid().ToString('N'))
$deployment = Join-Path $contentRoot ('.Contracts-staging-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $contentRoot ('.Contracts-backup-' + [Guid]::NewGuid().ToString('N'))
$published = $false
function Remove-DeploymentDirectory([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    $prefix = [IO.Path]::GetFullPath($contentRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a directory outside the content project.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
try {
    dotnet build $contractSource -c $Configuration --output $staging -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Contract project build failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $staging 'GalNet.Project.Contracts.dll'))) {
        throw 'Contract project must output GalNet.Project.Contracts.dll.'
    }
    # Replace the complete dependency set, so removed dependencies cannot linger.
    New-Item -ItemType Directory -Path $deployment | Out-Null
    Get-ChildItem -LiteralPath $staging | Copy-Item -Destination $deployment -Recurse
    if (Test-Path -LiteralPath $destination) {
        if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Contracts must be a regular project directory.'
        }
        # These exact sibling paths have been resolved under the selected project.
        [IO.Directory]::Move($destination, $backup)
    }
    try { [IO.Directory]::Move($deployment, $destination) }
    catch {
        if (Test-Path -LiteralPath $backup) { [IO.Directory]::Move($backup, $destination) }
        throw
    }
    $published = $true
    Write-Output (Join-Path $destination 'GalNet.Project.Contracts.dll')
}
finally {
    Remove-DeploymentDirectory $deployment
    if ($published) { Remove-DeploymentDirectory $backup }
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolvedStaging.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean staging outside the temporary directory.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
