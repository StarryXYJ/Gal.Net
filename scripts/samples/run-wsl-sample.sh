#!/usr/bin/env bash
# Developer helper for the local WSLg candidate, not a Linux release installer.
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
candidate_root="$repo_root/out/cross-platform-p3b-current"
sample_root="${GALNET_WSL_SAMPLE_ROOT:-$candidate_root/linux-developer}"
native_root="${GALNET_WSL_NATIVE_ROOT:-$candidate_root/deps/root/usr/lib/x86_64-linux-gnu}"
profile_root="${GALNET_WSL_PROFILE:-$candidate_root/manual-profile}"
check_only=false
if [[ "${1:-}" == "--check" ]]; then
    check_only=true
    shift
fi

if [[ ! -f "$sample_root/GalNet.Sample.Avalonia" || ! -f "$sample_root/pak/game.pak" ]]; then
    printf 'Missing Linux Sample or game content: %s\n' "$sample_root" >&2
    exit 1
fi
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    printf 'No WSLg display is available. Run this helper in a WSLg terminal.\n' >&2
    exit 1
fi

# The current WSL host defaults to llvmpipe despite having a D3D12-capable GPU.
# Keep the choice local to this process and honor an explicit caller preference.
if [[ -z "${GALLIUM_DRIVER:-}" && -e /dev/dxg ]]; then
    export GALLIUM_DRIVER=d3d12
fi
export LD_LIBRARY_PATH="$sample_root:$native_root${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
if [[ -z "${VLC_PLUGIN_PATH:-}" && -d "$native_root/vlc/plugins" ]]; then
    export VLC_PLUGIN_PATH="$native_root/vlc/plugins"
fi

printf 'Sample: %s\nProfile: %s\nGALLIUM_DRIVER=%s\n' \
    "$sample_root" "$profile_root" "${GALLIUM_DRIVER:-system default}"
if command -v glxinfo >/dev/null 2>&1; then
    glxinfo -B
else
    printf 'glxinfo is unavailable; the actual renderer has not been checked.\n' >&2
fi
if "$check_only"; then
    exit 0
fi

chmod u+x "$sample_root/GalNet.Sample.Avalonia"
cd -- "$sample_root"
exec ./GalNet.Sample.Avalonia --profile "$profile_root" "$@"
