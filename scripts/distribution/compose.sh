#!/bin/sh
set -eu
compose_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
compose_cli="$compose_directory/../GalNet.Editor.Headless"
if [ ! -x "$compose_cli" ] && [ -f "$compose_cli.exe" ]; then
    compose_cli="$compose_cli.exe"
fi
if [ ! -x "$compose_cli" ]; then
    printf '%s\n' 'Use Tools/compose.sh from the distributed Editor package, beside its self-contained CLI.' >&2
    exit 1
fi
exec "$compose_cli" compose "$@"
