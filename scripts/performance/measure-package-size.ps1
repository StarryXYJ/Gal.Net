[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$item = Get-Item -LiteralPath $Path
$files = if ($item.PSIsContainer) {
    @(Get-ChildItem -LiteralPath $item.FullName -File -Recurse | ForEach-Object {
        [pscustomobject]@{ Name = [IO.Path]::GetRelativePath($item.FullName, $_.FullName); Bytes = $_.Length }
    })
} else {
    @([pscustomobject]@{ Name = $item.Name; Bytes = $item.Length })
}
$entries = @()
if (-not $item.PSIsContainer -and $item.Extension -in '.apk', '.aab', '.zip') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($item.FullName)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object {
            [pscustomobject]@{ Name = $_.FullName; Bytes = $_.Length; CompressedBytes = $_.CompressedLength }
        } | Sort-Object CompressedBytes -Descending)
    } finally { $archive.Dispose() }
}
$report = [pscustomobject]@{
    Path = $item.FullName
    Kind = if ($item.PSIsContainer) { 'directory' } else { 'file' }
    TotalBytes = [long](($files | Measure-Object Bytes -Sum).Sum)
    FileCount = $files.Count
    Files = @($files | Sort-Object Bytes -Descending)
    ArchiveEntries = $entries
}
$json = $report | ConvertTo-Json -Depth 5
if ($OutputFile) { $json | Set-Content -LiteralPath $OutputFile -Encoding utf8 } else { $json }
