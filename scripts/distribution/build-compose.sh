#!/bin/sh
set -eu
build_compose_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
build_compose_cli="$build_compose_directory/../GalNet.Editor.Headless"
if [ ! -x "$build_compose_cli" ] && [ -f "$build_compose_cli.exe" ]; then
    build_compose_cli="$build_compose_cli.exe"
fi
if [ ! -x "$build_compose_cli" ]; then
    printf '%s\n' 'Use Tools/build-compose.sh from the distributed Editor package, beside its self-contained CLI.' >&2
    exit 1
fi
exec "$build_compose_cli" build-compose "$@"
