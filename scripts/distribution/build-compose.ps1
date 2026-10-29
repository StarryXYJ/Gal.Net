$ErrorActionPreference = 'Stop'
$buildComposeCli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless.exe'
if (!(Test-Path -LiteralPath $buildComposeCli -PathType Leaf)) {
    $buildComposeCli = Join-Path $PSScriptRoot '../GalNet.Editor.Headless'
}
if (!(Test-Path -LiteralPath $buildComposeCli -PathType Leaf)) {
    throw 'Use Tools/build-compose.ps1 from the distributed Editor package, beside its self-contained CLI.'
}
& $buildComposeCli build-compose @args
exit $LASTEXITCODE
