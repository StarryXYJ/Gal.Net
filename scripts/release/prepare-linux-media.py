#!/usr/bin/env python3
"""Prepare private VLC libraries from signed Ubuntu apt indexes without installing packages."""
import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import platform
import shlex
import shutil
import subprocess
import tempfile
import urllib.parse
import urllib.request


def run(arguments):
    return subprocess.check_output(arguments, text=True)


def paragraphs(text):
    for paragraph in text.split("\n\n"):
        yield dict(line.split(": ", 1) for line in paragraph.splitlines() if ": " in line and not line.startswith(" "))


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download_plan(text):
    items = []
    for line in text.splitlines():
        if not line.lstrip().startswith("'"):
            continue
        fields = shlex.split(line)
        if len(fields) == 4 and fields[1].endswith(".deb") and fields[2].isdigit():
            items.append(fields)
    return items


def prepare(work, output, refresh):
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        raise RuntimeError("Run on Ubuntu 24.04 x86_64, including WSL; this is not a cross-distribution resolver.")
    os_release = dict(line.split("=", 1) for line in pathlib.Path("/etc/os-release").read_text().splitlines() if "=" in line)
    if os_release.get("ID", "").strip('"') != "ubuntu" or os_release.get("VERSION_ID", "").strip('"') != "24.04":
        raise RuntimeError("The current media baseline is Ubuntu 24.04.")
    if output.exists():
        raise FileExistsError(f"Use a new media output directory: {output}")
    for directory in [work / "lists/partial", work / "cache/archives/partial", work / "downloads"]:
        directory.mkdir(parents=True, exist_ok=True)
    apt = ["-o", f"Dir::State::lists={work / 'lists'}", "-o", f"Dir::Cache={work / 'cache'}", "-o", "Debug::NoLocking=true"]
    if refresh:
        (work / "apt-update.log").write_text(run(["apt-get", *apt, "update"]))
    # Empty package status is essential: the release must include dependencies
    # even when they happen to be installed on this preparation machine.
    apt += ["-o", "Dir::State::status=/dev/null"]
    plan = run(["apt-get", *apt, "--no-install-recommends", "--print-uris", "--assume-yes", "--download-only",
                "install", "libvlc5", "vlc-plugin-base", "vlc-plugin-video-output"])
    (work / "plan.txt").write_text(plan)
    items = download_plan(plan)
    if not items:
        raise RuntimeError("apt did not produce a complete download plan:\n" + plan[-4000:])
    selections = []
    for _, filename, _, _ in items:
        name, version, _ = urllib.parse.unquote(filename.removesuffix(".deb")).rsplit("_", 2)
        selections.append(f"{name}={version}")
    metadata = {}
    for package in paragraphs(run(["apt-cache", *apt, "show", *selections])):
        if "SHA256" in package:
            metadata[(package["Package"], package["Version"])] = package

    def download(item):
        url, filename, size, _ = item
        name, version, _ = urllib.parse.unquote(filename.removesuffix(".deb")).rsplit("_", 2)
        package = metadata[(name, version)]
        destination = work / "downloads" / filename
        expected = package["SHA256"]
        if not destination.exists() or sha256(destination) != expected:
            temporary = destination.with_suffix(".download")
            # Mirrors occasionally time out; the signed-index SHA256 remains
            # authoritative when fetching the same archive from Ubuntu directly.
            canonical = "https://archive.ubuntu.com/ubuntu/" + package["Filename"]
            # GitHub images can use apt's mirror+file transport. urllib cannot
            # resolve that pseudo-URI; download the signed-index path directly.
            primary = url if urllib.parse.urlsplit(url).scheme in ("http", "https") else canonical
            urls = [primary, canonical, primary]
            for attempt, download_url in enumerate(urls):
                try:
                    with urllib.request.urlopen(download_url, timeout=30) as response, temporary.open("wb") as stream:
                        shutil.copyfileobj(response, stream)
                    break
                except (OSError, TimeoutError):
                    if attempt == len(urls) - 1:
                        raise
                    print(f"Retry download: {name}", flush=True)
            if temporary.stat().st_size != int(size) or sha256(temporary) != expected:
                raise RuntimeError(f"apt SHA256/size mismatch: {filename}")
            temporary.replace(destination)
        print(f"Verified {name} {version}", flush=True)
        return {"package": name, "version": version, "architecture": package["Architecture"],
                "sha256": expected, "url": url, "file": filename,
                "source": package.get("Source", name)}

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        packages = list(pool.map(download, items))
    extraction = pathlib.Path(tempfile.mkdtemp(prefix="extract-", dir=work))
    candidate = pathlib.Path(tempfile.mkdtemp(prefix="media-", dir=work))
    native = candidate / "native"
    licenses = candidate / "licenses"
    native.mkdir()
    licenses.mkdir()
    for package in packages:
        subprocess.run(["dpkg-deb", "-x", str(work / "downloads" / package["file"]), str(extraction)], check=True)

    def copy_file(source, target):
        resolved = source.resolve(strict=True)
        if not resolved.is_relative_to(extraction.resolve()):
            raise RuntimeError(f"A package symlink escapes extraction: {source}")
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() and sha256(target) != sha256(resolved):
            raise RuntimeError(f"Conflicting native library: {target.name}")
        shutil.copyfile(resolved, target)
        target.chmod(0o644)

    # Use host glibc and the host graphics stack. In particular, never distribute
    # Mesa drivers from the build machine over the user's GPU driver selection.
    excluded_prefixes = ("ld-linux", "libGL", "libEGL", "libGLES", "libOpenGL", "libglapi", "libGLdispatch",
                         "libgbm", "libdrm", "libLLVM", "libgallium", "libvulkan", "libVk", "libnvidia")
    glibc_prefixes = ("libc.so", "libm.so", "libmvec.so", "libdl.so", "librt.so", "libpthread.so", "libresolv.so",
                      "libutil.so", "libanl.so", "libBrokenLocale.so", "libnss_")
    library_roots = [extraction / "usr/lib/x86_64-linux-gnu", extraction / "lib/x86_64-linux-gnu"]
    for library_root in library_roots:
        if not library_root.exists():
            continue
        for source in sorted(library_root.rglob("*.so*")):
            if source.is_dir():
                continue
            relative = source.relative_to(library_root)
            if "vlc" in relative.parts:
                copy_file(source, native / relative)
            elif not any(part in relative.parts for part in ("dri", "gconv", "vdpau", "gbm", "caca")) and not source.name.startswith(excluded_prefixes + glibc_prefixes):
                copy_file(source, native / source.name)
    # A few legacy codec packages still install in /usr/lib rather than the
    # multiarch directory (for example sidplay/resid).
    for library_root in (extraction / "usr/lib", extraction / "lib"):
        if library_root.exists():
            for source in sorted(library_root.glob("*.so*")):
                if source.is_file() and not source.name.startswith(excluded_prefixes + glibc_prefixes):
                    copy_file(source, native / source.name)
    for package in packages:
        copyright_file = extraction / "usr/share/doc" / package["package"] / "copyright"
        if not copyright_file.exists():
            raise RuntimeError(f"Missing package copyright: {package['package']}")
        copy_file(copyright_file, licenses / (package["package"] + ".copyright"))
    for required in ("libvlc.so.5", "libvlccore.so.9", "vlc/plugins/codec/libavcodec_plugin.so"):
        if not (native / required).is_file():
            raise RuntimeError(f"Missing VLC payload: {required}")
    manifest = {"schemaVersion": 1, "baseline": "Ubuntu 24.04 x86_64", "packages": packages,
                "systemDependencies": "Host glibc, graphics drivers, X11/Wayland display, fonts and audio service remain platform requirements.",
                "sources": "Exact Ubuntu source package/version is recorded per package; obtain corresponding sources from https://launchpad.net/ubuntu/+source before public redistribution."}
    (candidate / "media-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    output.parent.mkdir(parents=True, exist_ok=True)
    candidate.rename(output)
    # All recursive cleanup targets are freshly-created children of our workspace.
    assert extraction.resolve().parent == work.resolve()
    shutil.rmtree(extraction)
    print(f"Media candidate: {output}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-directory", type=pathlib.Path, required=True)
    parser.add_argument("--output-directory", type=pathlib.Path, required=True)
    parser.add_argument("--no-refresh", action="store_true")
    arguments = parser.parse_args()
    prepare(arguments.work_directory.resolve(), arguments.output_directory.resolve(), not arguments.no_refresh)
