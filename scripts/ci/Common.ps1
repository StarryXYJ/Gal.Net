$ciRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Get-DesktopProjects([string]$RepositoryPath = $ciRepository) {
    [xml]$solution = Get-Content -LiteralPath (Join-Path $RepositoryPath 'GalNet.slnx')
    @($solution.SelectNodes('//Project') | ForEach-Object { $_.Path.Replace('\', '/') } |
        Where-Object { !$_.StartsWith('src/Samples/GalNet.Sample.CrossPlatform/', [StringComparison]::Ordinal) })
}

function New-DesktopSolutionFilter([string]$Path, [string[]]$Projects, [string]$RepositoryPath = $ciRepository) {
    $available = @(Get-DesktopProjects $RepositoryPath)
    if (!$Projects) { $Projects = $available }
    foreach ($project in $Projects) {
        if ($project -cnotin $available) { throw "Project is outside the desktop solution: $project" }
    }
    $Path = [IO.Path]::GetFullPath($Path)
    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    [ordered]@{ solution = [ordered]@{
        path = (Join-Path $RepositoryPath 'GalNet.slnx')
        projects = @($Projects)
    } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8
    return $Path
}

function Write-ActionOutput([string]$Name, [string]$Value, [string]$Path = $env:GITHUB_OUTPUT) {
    if ($Path) { "$Name=$Value" | Add-Content -LiteralPath $Path -Encoding utf8 }
}
