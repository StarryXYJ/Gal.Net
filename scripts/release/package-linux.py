#!/usr/bin/env python3
"""Assemble Ubuntu 24.04 Linux x64 candidates from self-contained Desktop publishes."""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

REPO = pathlib.Path(__file__).resolve().parents[2]
APPS = {"Sample": "GalNet.Sample.Avalonia", "Editor": "GalNet.Editor", "CLI": "GalNet.Editor.Headless"}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n")


def copy_tree(source, destination):
    for path in sorted(source.rglob("*")):
        if path.is_symlink():
            raise RuntimeError(f"Unexpected input link: {path}")
        target = destination / path.relative_to(source)
        if path.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists() and digest(target) != digest(path):
                raise RuntimeError(f"Conflicting payload: {target}")
            shutil.copyfile(path, target)
            target.chmod(0o644)


def merge_publish(source, package, kind):
    for path in sorted(source.iterdir()):
        if path.is_symlink():
            raise RuntimeError(f"Unexpected input link: {path}")
        if path.name.endswith(".pdb"):
            continue
        if path.name.lower().startswith("readme"):
            target = package / "docs" / (kind + "-" + path.name)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
        elif path.name == "Skills":
            copy_tree(path, package / "Skills")
        elif path.is_dir():
            copy_tree(path, package / "lib" / path.name)
        else:
            target = package / "lib" / path.name
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists() and digest(target) != digest(path):
                raise RuntimeError(f"Editor/CLI dependency differs: {path.name}")
            shutil.copyfile(path, target)
            target.chmod(0o644)
    executable = package / "lib" / APPS[kind]
    header = executable.read_bytes()[:20]
    if header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 62:
        raise RuntimeError(f"Expected a Linux x64 ELF apphost: {executable}")
    executable.chmod(0o755)
    launcher = package / APPS[kind]
    launcher.write_text('''#!/bin/sh
set -eu
app_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export LD_LIBRARY_PATH="$app_root/lib/native:$app_root/lib/native/vlc:$app_root/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export VLC_PLUGIN_PATH="$app_root/lib/native/vlc/plugins"
if [ -e /dev/dxg ] && [ -z "${GALLIUM_DRIVER:-}" ]; then
    export GALLIUM_DRIVER=d3d12
fi
exec "$app_root/lib/APP_NAME" "$@"
'''.replace("APP_NAME", APPS[kind]))
    launcher.chmod(0o755)


def capture_files(root):
    return [{"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": digest(path)}
            for path in sorted(root.rglob("*")) if path.is_file() and path.relative_to(root).as_posix() != "docs/release-manifest.json"]


def verify_native(root):
    native = root / "lib/native"
    environment = dict(os.environ, LD_LIBRARY_PATH=f"{native}:{native / 'vlc'}:{root / 'lib'}")
    missing = []
    libraries = [*(root / "lib").glob("*.so*"), *native.rglob("*.so*")]
    for path in sorted(libraries):
        with path.open("rb") as stream:
            header = stream.read(20)
        if header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 62:
            raise RuntimeError(f"Wrong native architecture: {path}")
        # CoreCLR loads this optional LTTng provider only for Linux system
        # tracing. Its legacy liblttng-ust.so.0 ABI is not a gameplay dependency;
        # preserve the runtime file without claiming PerfCollect support.
        if path.parent == root / "lib" and path.name == "libcoreclrtraceptprovider.so":
            continue
        result = subprocess.run(["ldd", str(path)], capture_output=True, text=True, env=environment)
        if "not found" in result.stdout or result.returncode != 0:
            missing.append({"path": path.relative_to(root).as_posix(), "output": "\n".join(
                line for line in (result.stdout + result.stderr).splitlines() if "not found" in line) or result.stderr})
    if missing:
        raise RuntimeError("Incomplete native dependencies: " + json.dumps(missing))


def archive(root, output):
    with tarfile.open(output, "w:gz") as tar:
        for path in [root, *sorted(root.rglob("*"))]:
            if path.is_symlink():
                raise RuntimeError(f"Unexpected output link: {path}")
            info = tar.gettarinfo(str(path), arcname=str(path.relative_to(root.parent)))
            info.uid = info.gid = info.mtime = 0
            info.uname = info.gname = ""
            if path.is_file():
                with path.open("rb") as stream:
                    tar.addfile(info, stream)
            else:
                tar.addfile(info)


def package(raw, media, output, content):
    if output.exists():
        raise FileExistsError(f"Use a new release output: {output}")
    for source in (raw, media, content):
        if source and output.is_relative_to(source):
            raise ValueError("Release output must be outside every input directory.")
    metadata = json.loads((raw / "publish.json").read_text(encoding="utf-8-sig"))
    version = metadata["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-(?:dev|preview)\.\d+)?", version):
        raise RuntimeError("Invalid candidate version.")
    if metadata["runtime"] != "linux-x64":
        raise RuntimeError("Expected linux-x64 publishes.")
    if not (media / "media-manifest.json").is_file():
        raise RuntimeError("Prepare the media closure first.")
    output.parent.mkdir(parents=True, exist_ok=True)
    work = pathlib.Path(tempfile.mkdtemp(prefix=".linux-package-", dir=output.parent))
    assets = work / "candidate/assets"
    assets.mkdir(parents=True)
    roots = {}
    flavor = metadata["sampleFlavor"]
    for kind in ("Sample", "Editor"):
        root = work / f"GalNet-{kind}-{version}-linux-x64".replace(version, version + ("-developer" if kind == "Sample" and flavor == "Developer" else ""))
        root.mkdir()
        roots[kind] = root
        merge_publish(raw / kind, root, kind)
        if kind == "Editor":
            merge_publish(raw / "CLI", root, "CLI")
            (root / "Tools").mkdir()
            for script in ("compose.sh", "compose.ps1", "build-compose.sh", "build-compose.ps1"):
                shutil.copyfile(REPO / "scripts/distribution" / script, root / "Tools" / script)
                (root / "Tools" / script).chmod(0o755 if script.endswith(".sh") else 0o644)
        copy_tree(media / "native", root / "lib/native")
        copy_tree(media / "licenses", root / "docs/licenses")
        shutil.copyfile(media / "media-manifest.json", root / "docs/native-media.json")
        shutil.copyfile(REPO / "LICENSE", root / "docs/LICENSE")
        write_json(root / "docs/application-layout.json", {"schemaVersion": 1, "runtimeIdentifier": "linux-x64",
                   "entryPoint": APPS[kind], "managedDirectory": "lib",
                   "contractsDirectory": "lib/DefaultContracts" if kind == "Editor" else "lib", "contentDirectory": "pak"})
        for directory in ("I18n", "ResourcePatches", "StoryPatches"):
            (root / "pak" / directory).mkdir(parents=True)
        (root / "docs/README.txt").write_text(
            f"GalNet {kind} {version} Linux x64 local candidate\n"
            f"Run ../{APPS[kind]}; keep the complete directory together.\n"
            "Ubuntu 24.04 x64 baseline; no dotnet SDK/runtime installation required.\n"
            "Host glibc, ICU, display/graphics drivers, CJK fonts and an audio service are required.\n"
            "Private media libraries and package copyright notices are in lib/native and docs/licenses.\n"
            "Exact Ubuntu source packages are recorded in native-media.json; source availability/licensing must be reviewed before public redistribution.\n"
            "WSLg selects d3d12 only when /dev/dxg exists and GALLIUM_DRIVER is unset.\n"
            "compose accepts Windows ZIP and Linux tar.gz runtimes; build-compose publishes explicitly selected application projects with an SDK.\n")
        print(f"Checking native closure: {kind}", flush=True)
        verify_native(root)
    if content:
        inspection = subprocess.run([str(roots["Editor"] / APPS["CLI"]), "game", "inspect", str(content)],
                                    capture_output=True, text=True, check=True)
        report = json.loads(inspection.stdout)
        if not report.get("success"):
            raise RuntimeError("Invalid compiled game content: " + inspection.stdout)
        descriptor = report["data"]
        expected = [*descriptor["basePaks"], descriptor["localizationPak"]]
        for story in descriptor["stories"]:
            expected.extend(story["parts"])
        for entry in expected:
            relative = pathlib.PurePosixPath(entry["path"])
            if relative.is_absolute() or ".." in relative.parts or "\\" in entry["path"]:
                raise RuntimeError("Invalid content payload path.")
            path = content / relative
            if path.is_symlink() or path.stat().st_size != entry["size"] or digest(path) != entry["sha256"]:
                raise RuntimeError(f"Content payload hash mismatch: {relative}")
        for entry in descriptor["contractFiles"]:
            if entry["path"] != pathlib.PurePosixPath(entry["path"]).name:
                raise RuntimeError("Invalid contract path.")
            path = roots["Sample"] / "lib" / entry["path"]
            if path.stat().st_size != entry["size"] or digest(path) != entry["sha256"]:
                raise RuntimeError(f"Content/runtime contract mismatch: {entry['path']}")
        game = work / roots["Sample"].name.replace("GalNet-Sample-", "GalNet-Sample-GameTestCase-")
        copy_tree(roots["Sample"], game)
        copy_tree(content, game / "pak")
        roots["Sample-GameTestCase"] = game
    source_commit = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"], text=True).strip()
    for kind, root in roots.items():
        write_json(root / "docs/release-manifest.json", {"schemaVersion": 1, "product": "GalNet", "version": version,
                   "package": kind, "runtime": "linux-x64", "selfContained": True, "readyToRun": False,
                   "sampleFlavor": flavor if kind.startswith("Sample") else None, "sourceCommit": source_commit,
                   "nativeEntryPoint": "lib/" + APPS["Editor" if kind == "Editor" else "Sample"], "files": capture_files(root)})
        archive(root, assets / (root.name + ".tar.gz"))
        print(f"Archived {root.name}", flush=True)
    contracts = ("GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "GalNet.Project.Contracts.dll")
    with zipfile.ZipFile(assets / f"GalNet-Sample-Contracts-{version}-linux-x64.zip", "w", compression=zipfile.ZIP_DEFLATED) as contract_zip:
        for name in contracts:
            contract_zip.write(raw / "Sample" / name, name)
        contract_zip.write(REPO / "LICENSE", "LICENSE")
    files = [{"path": "assets/" + path.name, "size": path.stat().st_size, "sha256": digest(path)} for path in sorted(assets.iterdir())]
    write_json(work / "candidate/linux-release-index.json", {"schemaVersion": 1, "version": version, "runtime": "linux-x64",
               "candidate": True, "sourceCommit": source_commit, "sampleFlavor": flavor, "files": files})
    (work / "candidate/SHA256SUMS").write_text("".join(f"{file['sha256']}  {file['path']}\n" for file in files))
    (work / "candidate").rename(output)
    print(f"Linux candidate: {output}\nEvidence/staging: {work}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish-directory", type=pathlib.Path, required=True)
    parser.add_argument("--media-directory", type=pathlib.Path, required=True)
    parser.add_argument("--output-directory", type=pathlib.Path, required=True)
    parser.add_argument("--content", type=pathlib.Path)
    args = parser.parse_args()
    package(args.publish_directory.resolve(), args.media_directory.resolve(), args.output_directory.resolve(), args.content.resolve() if args.content else None)
