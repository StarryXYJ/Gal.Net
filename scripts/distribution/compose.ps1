$ErrorActionPreference = 'Stop'
$composeCli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless.exe'
if (!(Test-Path -LiteralPath $composeCli -PathType Leaf)) {
    $composeCli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless'
}
if (!(Test-Path -LiteralPath $composeCli -PathType Leaf)) {
    throw 'Use Tools/compose.ps1 from the distributed Editor package, beside its self-contained CLI.'
}
& $composeCli compose @args
exit $LASTEXITCODE
