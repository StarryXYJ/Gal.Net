"""Prepare pinned SDL input-probe libraries without installing system packages."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def archive(spec, supplied, work):
    path = Path(supplied) if supplied else work / spec["url"].rsplit("/", 1)[1]
    if not supplied:
        with urllib.request.urlopen(spec["url"], timeout=60) as response, path.open("wb") as output:
            shutil.copyfileobj(response, output)
    if digest(path) != spec["sha256"]:
        raise ValueError(f"SDL archive SHA256 mismatch: {path}")
    return path


def run(args, log):
    with log.open("w", encoding="utf-8") as stream:
        subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=["win-x64", "linux-x64"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-archive")
    parser.add_argument("--windows-archive")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Native probe output must be empty; existing files are preserved.")
    sources = json.loads(Path(__file__).with_name("native-sources.json").read_text())
    # Windows sandbox temp ACLs can reject recursive cleanup. Keep its work
    # under the explicitly selected output parent; Linux builds use fast /tmp.
    work_parent = output.parent if platform.system() == "Windows" else Path(tempfile.gettempdir()).resolve()
    with tempfile.TemporaryDirectory(prefix="galnet-sdl3-native-", dir=work_parent) as temporary:
        work = Path(temporary)
        if work.resolve().parent != work_parent or not work.name.startswith("galnet-sdl3-native-"):
            raise ValueError("Unexpected native build temporary directory.")
        source_archive = archive(sources["source"], args.source_archive, work)
        source_root = "SDL3-" + sources["version"]
        with tarfile.open(source_archive) as source_tar:
            with source_tar.extractfile(source_root + "/LICENSE.txt") as license_stream:
                (output / "SDL-LICENSE.txt").write_bytes(license_stream.read())
            if args.target == "linux-x64":
                source_tar.extractall(work, filter="data")
        source = work / source_root
        configuration = []
        if args.target == "win-x64":
            binary_archive = archive(sources["win-x64"], args.windows_archive, work)
            with zipfile.ZipFile(binary_archive) as binary:
                with binary.open("SDL3.dll") as stream, (output / "SDL3.dll").open("wb") as dest:
                    shutil.copyfileobj(stream, dest)
            library = output / "SDL3.dll"
        else:
            if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
                raise ValueError("linux-x64 probe requires a native Linux x64 compiler.")
            # This deliberately probes an input-only build. Production deployment
            # and physical controller drivers have their own acceptance criteria.
            configuration = ["-DSDL_AUDIO=OFF", "-DSDL_VIDEO=OFF", "-DSDL_RENDER=OFF",
                             "-DSDL_GPU=OFF", "-DSDL_CAMERA=OFF", "-DSDL_DIALOG=OFF",
                             "-DSDL_UNIX_CONSOLE_BUILD=ON", "-DSDL_SHARED=ON",
                             "-DSDL_STATIC=OFF", "-DSDL_TEST_LIBRARY=OFF",
                             "-DSDL_TESTS=OFF", "-DSDL_EXAMPLES=OFF"]
            build = work / "build"
            run(["cmake", "-S", str(source), "-B", str(build), "-G", "Ninja",
                 "-DCMAKE_BUILD_TYPE=Release", *configuration], output / "configure.log")
            run(["cmake", "--build", str(build), "--parallel", "4"], output / "build.log")
            library = output / "libSDL3.so.0"
            shutil.copy2(build / "libSDL3.so.0", library)
            run(["ldd", str(library)], output / "ldd.log")
        record = {"version": sources["version"], "target": args.target,
                  "library": library.name, "sha256": digest(library),
                  "release": sources["release"], "sources": sources,
                  "configuration": configuration, "platform": platform.platform(),
                  "physicalControllerTested": False}
        (output / "native.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record, indent=2))


if __name__ == "__main__":
    main()
