[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$Contracts,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$Cli = [IO.Path]::GetFullPath($Cli)
$Contracts = [IO.Path]::GetFullPath($Contracts)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a new verification directory.' }
New-Item -ItemType Directory -Path $WorkDirectory | Out-Null
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$scriptPath = Join-Path $repository 'scripts/release/Publish.ps1'
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Publish script does not parse.' }
# Load the actual packaging helpers without running the build/publish entry point.
foreach ($name in @('Get-RelativePath', 'Copy-ContentTree', 'Add-GameContent')) {
    $definition = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$headlessProject = 'unused-build-project'
function Invoke-DotNet([string[]]$Arguments) {
    $separator = [Array]::IndexOf($Arguments, '--')
    if ($separator -lt 0 -or $Arguments[$separator + 1] -ne 'galpak' -or $Arguments[$separator + 2] -ne 'install') {
        throw 'This verification may only run the distributed galpak installer.'
    }
    & $Cli @($Arguments[($separator + 1)..($Arguments.Length - 1)]) | Out-Null
    if ($LASTEXITCODE) { throw 'Distributed installation failed.' }
}
function Invoke-Cli([string[]]$Arguments) {
    $output = & $Cli @Arguments
    if ($LASTEXITCODE) { $output | Write-Host; throw "CLI failed: $Arguments" }
}
$project = Join-Path $WorkDirectory 'StoryFixture'
Invoke-Cli @('project', 'create', $project, '--contracts', $Contracts)
foreach ($generated in Get-ChildItem -LiteralPath (Join-Path $project 'Graph/groups') -Filter '*.rawgalgroup' -File) {
    Remove-Item -LiteralPath $generated.FullName
}
@'
{"version":2,"name":"Story","rootNodeId":"base","nodes":[{"id":"base","name":"Base","type":"Group","file":"groups/base.rawgalgroup"},{"id":"chapter","name":"Chapter","type":"Group","file":"groups/chapter.rawgalgroup","storyPackage":"extra"},{"id":"end","name":"End","type":"Group","file":"groups/end.rawgalgroup"}],"edges":[{"id":"first","fromNodeId":"base","fromOutlet":0,"toNodeId":"chapter"},{"id":"second","fromNodeId":"chapter","fromOutlet":0,"toNodeId":"end"}],"playerVariables":[],"saveVariables":[]}
'@ | Set-Content -LiteralPath (Join-Path $project 'Graph/graph.json') -Encoding utf8
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Graph/groups') | Out-Null
foreach ($group in @('base', 'chapter', 'end')) {
    '{"version":2,"kind":"Raw","entries":[]}' | Set-Content -LiteralPath (Join-Path $project "Graph/groups/$group.rawgalgroup") -Encoding utf8
}
$packages = Join-Path $WorkDirectory 'packages'
Invoke-Cli @('export', $project, '--output', $packages)
$Galpak = (Get-ChildItem -LiteralPath $packages -Filter '*.galpak').FullName
$zip = [IO.Compression.ZipFile]::OpenRead($Galpak)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('pak/content.json').Open())
    try { $content = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
finally { $zip.Dispose() }
$storyFiles = @(Get-ChildItem -LiteralPath (Join-Path $packages "StoryPatches/$($content.buildId)") -Filter '*.pak' -File)
if (!$storyFiles.Count) { throw 'Fixture did not produce external story PAKs.' }
$unrelatedBuild = 'b' * 64
New-Item -ItemType Directory -Path (Join-Path $packages "StoryPatches/$unrelatedBuild"), (Join-Path $packages 'ResourcePatches') | Out-Null
'unrelated build' | Set-Content (Join-Path $packages "StoryPatches/$unrelatedBuild/unused.txt")
'external resource package notes' | Set-Content (Join-Path $packages 'ResourcePatches/notes.txt')
$payload = Join-Path $WorkDirectory 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Add-GameContent $WorkDirectory $payload $Contracts
$installed = Join-Path $payload 'pak'
if ((Test-Path (Join-Path $installed 'pak')) -or !(Test-Path (Join-Path $installed 'content.json'))) { throw 'Incorrect content root.' }
foreach ($file in $storyFiles) {
    $copied = Join-Path $installed "StoryPatches/$($content.buildId)/$($file.Name)"
    if ((Get-FileHash -LiteralPath $copied).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw 'Story package changed.' }
}
if (Test-Path (Join-Path $installed "StoryPatches/$unrelatedBuild")) { throw 'Copied another build.' }
if (!(Test-Path (Join-Path $installed 'ResourcePatches/notes.txt'))) { throw 'Missing external resource package directory.' }
[ordered]@{ passed = $true; storyParts = $storyFiles.Count; contentRoot = $installed; unrelatedBuildExcluded = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $WorkDirectory 'verification.json') -Encoding utf8
Write-Host "Galpak integration passed: $installed"
