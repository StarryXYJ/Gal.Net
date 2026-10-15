[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Root,

    [switch]$IncludeExcludedDirectories,

    [switch]$Json
)

<#
.SYNOPSIS
    Counts physical and non-empty lines in the project's source and documentation files.

.DESCRIPTION
    Files are grouped into four categories:
      - Tests: paths below test/tests directories or projects whose name ends in .Test/.Tests.
      - Editor: paths below an Editor directory or with an .Editor. name segment.
      - Documentation: Markdown and other documentation files, including README/LICENSE files.
      - Other: all other recognized source files.

    The script deliberately counts physical lines. It does not try to remove comments because
    comment syntax differs between C#, XAML, PowerShell, shaders, and other supported languages.
    Files with extensions outside the lists below are ignored.

.PARAMETER Root
    Directory to scan. Defaults to the repository root (the parent of this script's directory).

.PARAMETER IncludeExcludedDirectories
    Also scan common generated, dependency, cache, and agent-tooling directories. This can make
    the result much larger and is normally not desirable for project source metrics.

.PARAMETER Json
    Writes machine-readable JSON instead of the human-readable tables.

.EXAMPLE
    .\scripts\count-code-lines.ps1

.EXAMPLE
    .\scripts\count-code-lines.ps1 -Root . -Json
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Join-Path $PSScriptRoot '..'
}

$rootPath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Root).Path)
if (-not [System.IO.Directory]::Exists($rootPath))
{
    throw "Root is not a directory: $rootPath"
}

$sourceExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @(
        '.cs', '.xaml', '.axaml', '.fs', '.fsx', '.vb',
        '.c', '.cc', '.cpp', '.cxx', '.h', '.hh', '.hpp', '.hxx',
        '.js', '.jsx', '.ts', '.tsx', '.py', '.ps1', '.psm1', '.sh', '.bash',
        '.sql', '.glsl', '.hlsl', '.sksl', '.props', '.targets'))
{
    [void]$sourceExtensions.Add($extension)
}

$documentationExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @('.md', '.mdx', '.rst', '.adoc', '.txt'))
{
    [void]$documentationExtensions.Add($extension)
}

$excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($directoryName in @(
        '.git', '.vs', '.vscode', '.agents', '.codex', '.aws',
        'bin', 'obj', 'artifacts', 'packages', 'node_modules',
        'Debug', 'DebugPublic', 'Release', 'Releases',
        'Temp', 'Library', 'Logs', 'BuildLog',
        'Output', 'Output-new', '.galnet'))
{
    [void]$excludedDirectoryNames.Add($directoryName)
}

function Get-RelativePath([string]$FullName)
{
    $relative = $FullName.Substring($rootPath.Length)
    return $relative.TrimStart([char]92, [char]47)
}

function Test-IsExcludedPath([string]$RelativePath)
{
    if ($IncludeExcludedDirectories)
    {
        return $false
    }

    foreach ($segment in ($RelativePath -split '[\\/]'))
    {
        if ($excludedDirectoryNames.Contains($segment))
        {
            return $true
        }
    }

    return $false
}

function Test-IsDocumentation([System.IO.FileInfo]$File, [string]$Extension)
{
    if ($documentationExtensions.Contains($Extension))
    {
        return $true
    }

    return $File.Name -match '^(README|LICENSE|NOTICE)(\..*)?$'
}

function Get-Category([System.IO.FileInfo]$File, [string]$RelativePath, [string]$Extension)
{
    if (Test-IsDocumentation $File $Extension)
    {
        return 'Documentation'
    }

    # Test paths are checked before Editor paths because names such as
    # test/GalNet.Editor.Tests contain both markers.
    if ($RelativePath -match '(?i)(^|[\\/])(test|tests)([\\/]|$)' -or
        $RelativePath -match '(?i)(^|[\\/])[^\\/]+\.Tests?([\\/]|$)')
    {
        return 'Tests'
    }

    if ($RelativePath -match '(?i)(^|[\\/])Editor([\\/]|$)' -or
        $RelativePath -match '(?i)\.Editor(\.|[\\/]|$)')
    {
        return 'Editor'
    }

    return 'Other'
}

function Get-LineCounts([string]$Path)
{
    $physicalLines = [int64]0
    $nonEmptyLines = [int64]0

    foreach ($line in [System.IO.File]::ReadLines($Path))
    {
        $physicalLines++
        if (-not [string]::IsNullOrWhiteSpace($line))
        {
            $nonEmptyLines++
        }
    }

    return [pscustomobject]@{
        Lines = $physicalLines
        NonEmptyLines = $nonEmptyLines
    }
}

function Get-Sum([object[]]$Items, [string]$Property)
{
    if ($null -eq $Items -or $Items.Count -eq 0)
    {
        return [int64]0
    }

    return [int64](($Items | Measure-Object -Property $Property -Sum).Sum)
}

$records = [System.Collections.Generic.List[object]]::new()
$skippedFiles = [System.Collections.Generic.List[string]]::new()

foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force)
{
    if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
    {
        continue
    }

    $relativePath = Get-RelativePath $file.FullName
    if (Test-IsExcludedPath $relativePath)
    {
        continue
    }

    $extension = $file.Extension
    $isDocumentation = Test-IsDocumentation $file $extension
    if (-not $isDocumentation -and -not $sourceExtensions.Contains($extension))
    {
        continue
    }

    try
    {
        $lineCounts = Get-LineCounts $file.FullName
    }
    catch
    {
        [void]$skippedFiles.Add($relativePath)
        Write-Warning "Unable to read '$relativePath': $($_.Exception.Message)"
        continue
    }

    $displayExtension = if ([string]::IsNullOrEmpty($extension)) { '(none)' } else { $extension }
    [void]$records.Add([pscustomobject]@{
        Category = Get-Category $file $relativePath $extension
        Extension = $displayExtension
        File = $relativePath
        Lines = $lineCounts.Lines
        NonEmptyLines = $lineCounts.NonEmptyLines
    })
}

$categories = @('Editor', 'Tests', 'Documentation', 'Other')
$summary = foreach ($category in $categories)
{
    $categoryRecords = @($records | Where-Object Category -eq $category)
    [pscustomobject]@{
        Category = $category
        Files = [int64]$categoryRecords.Count
        Lines = Get-Sum $categoryRecords 'Lines'
        NonEmptyLines = Get-Sum $categoryRecords 'NonEmptyLines'
    }
}

$total = [pscustomobject]@{
    Category = 'Total'
    Files = Get-Sum $summary 'Files'
    Lines = Get-Sum $summary 'Lines'
    NonEmptyLines = Get-Sum $summary 'NonEmptyLines'
}

$byExtension = foreach ($category in $categories)
{
    $categoryRecords = @($records | Where-Object Category -eq $category)
    foreach ($group in @($categoryRecords | Group-Object Extension | Sort-Object Name))
    {
        [pscustomobject]@{
            Category = $category
            Extension = $group.Name
            Files = [int64]$group.Count
            Lines = Get-Sum @($group.Group) 'Lines'
            NonEmptyLines = Get-Sum @($group.Group) 'NonEmptyLines'
        }
    }
}

if ($Json)
{
    [pscustomobject]@{
        Root = $rootPath
        Summary = @($summary)
        Total = $total
        ByExtension = @($byExtension)
        SkippedFiles = @($skippedFiles)
    } | ConvertTo-Json -Depth 5
    exit 0
}

Write-Host "Root: $rootPath" -ForegroundColor Cyan
Write-Host "Counting physical lines and non-empty lines; comments are included." -ForegroundColor DarkGray
Write-Host ''
$summary | Format-Table -AutoSize
Write-Host 'Total:' -ForegroundColor Cyan
$total | Format-Table -AutoSize
Write-Host 'By extension:' -ForegroundColor Cyan
$byExtension | Format-Table -AutoSize

if ($skippedFiles.Count -gt 0)
{
    Write-Warning ("Skipped {0} unreadable file(s). Use -Json to inspect the list." -f $skippedFiles.Count)
}
