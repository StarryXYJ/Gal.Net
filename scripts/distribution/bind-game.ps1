[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Cli
)
$ErrorActionPreference = 'Stop'
$Package = (Resolve-Path -LiteralPath $Package).Path
if (!$Cli) {
    $Cli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless.exe'
    if (!(Test-Path -LiteralPath $Cli)) { $Cli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless' }
}
$Cli = (Resolve-Path -LiteralPath $Cli).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new binding output directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$content = Join-Path $OutputDirectory 'pak'
& $Cli galpak install $Package --output $content *> (Join-Path $OutputDirectory 'install.log')
if ($LASTEXITCODE -ne 0) { throw 'Game package validation/installation failed; see install.log.' }
$inspection = & $Cli game inspect $content
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the verified game package.' }
$result = ($inspection -join "`n") | ConvertFrom-Json
if (!$result.success -or !$result.data.projectId) { throw 'Game package did not report a project identity.' }
function Escape-MSBuild([string]$Value) {
    [regex]::Replace($Value, '[%$@();?*'']', { param($match) '%' + ([int][char]$match.Value).ToString('X2') })
}
$document = [xml]'<Project><PropertyGroup /></Project>'
$group = $document.SelectSingleNode('/Project/PropertyGroup')
foreach ($entry in ([ordered]@{ GalNetPlayerMode = 'Bound'; GalNetGamePackage = $Package; GalNetBoundProjectId = [string]$result.data.projectId }).GetEnumerator()) {
    $element = $document.CreateElement($entry.Key)
    $element.InnerText = Escape-MSBuild $entry.Value
    [void]$group.AppendChild($element)
}
$path = Join-Path $OutputDirectory 'GameBinding.props'
$document.Save($path)
Write-Host "Verified binding: $($result.data.projectId). Import $path before GalNet.Player.props; distribute $content as pak/."
