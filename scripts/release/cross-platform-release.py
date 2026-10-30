"""Package validated desktop/Android publishes and seal the complete release matrix."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import tarfile
import zipfile

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "scripts/ci"))
spec = importlib.util.spec_from_file_location("publisher", REPO / "scripts/ci/publish-applications.py")
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)
CONTRACTS = ("GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "GalNet.Project.Contracts.dll")


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(root):
    return [{"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": digest(path)}
            for path in sorted(root.rglob("*")) if path.is_file()]


def verify_files(root, files):
    seen = set()
    for item in files:
        relative = Path(item["path"])
        path = (root / relative).resolve()
        if "\\" in item["path"] or relative.is_absolute() or not path.is_relative_to(root.resolve()) or path == root.resolve():
            raise ValueError("Invalid release payload path.")
        if item["path"] in seen or path.is_symlink() or path.stat().st_size != item["size"] or digest(path) != item["sha256"]:
            raise ValueError("Release payload changed: " + item["path"])
        seen.add(item["path"])


def copy_tree(source, target):
    for path in sorted(source.rglob("*")):
        if path.is_symlink():
            raise ValueError("Release payload cannot contain symlinks.")
        if path.is_dir() or path.suffix == ".pdb":
            continue
        relative = path.relative_to(source)
        if len(relative.parts) == 1 and path.name.lower().startswith("readme"):
            continue
        if "libvlc" in relative.parts and any(part in ("win-x86", "win-arm64") for part in relative.parts):
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists() and digest(destination) != digest(path):
            raise ValueError("Application dependency collision: " + str(relative))
        shutil.copyfile(path, destination)
        destination.chmod(0o644)


def archive(root, output, linux):
    files = inventory(root)
    if linux:
        with tarfile.open(output, "w:gz", compresslevel=3) as tar:
            tar.add(root, arcname=root.name)
        with tarfile.open(output) as tar:
            members = {item.name: item for item in tar.getmembers()}
            for item in files:
                member = members[root.name + "/" + item["path"]]
                with tar.extractfile(member) as stream:
                    if hashlib.file_digest(stream, "sha256").hexdigest() != item["sha256"]:
                        raise ValueError("Archive verification failed.")
                if item["path"].startswith("GalNet.") and not member.mode & 0o111:
                    raise ValueError("Launcher lost executable permissions.")
    else:
        with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=3) as zipped:
            for path in sorted(root.rglob("*")):
                zipped.write(path, path.relative_to(root).as_posix())
        with zipfile.ZipFile(output) as zipped:
            for item in files:
                with zipped.open(item["path"]) as stream:
                    if hashlib.file_digest(stream, "sha256").hexdigest() != item["sha256"]:
                        raise ValueError("Archive verification failed.")


def asset_name(product, version, rid, deployment):
    suffix = "" if deployment == "SelfContained" else "-framework-dependent"
    extension = ".tar.gz" if rid == "linux-x64" else ".zip"
    return f"GalNet-{product}-{version}-{rid}{suffix}{extension}"


def expected_assets(version):
    return {asset_name(product, version, rid, deployment)
            for rid in ("win-x64", "linux-x64")
            for deployment in ("SelfContained", "FrameworkDependent")
            for product in ("Editor", "CrossPlatform-Sample-GameTestCase", "CrossPlatform-Sample-Player", "CrossPlatform-Sample-Developer")} | {
                f"GalNet-CrossPlatform-Sample-Contracts-{version}-{rid}.zip" for rid in ("win-x64", "linux-x64")} | {
                f"GalNet-CrossPlatform-Sample-GameTestCase-{version}-android-arm64.apk"}


def desktop(raw, output, media=None):
    report = read(raw / "application-publish.json")
    if not report["passed"] or not report.get("boundProjectId"):
        raise ValueError("A successful, game-bound application publish is required.")
    if output.exists():
        raise FileExistsError("Use a fresh package output.")
    rid, version, deployment = report["target"], report["version"], report["deployment"]
    linux = rid == "linux-x64"
    if linux and (media is None or not (media / "media-manifest.json").is_file()):
        raise ValueError("Prepare the private Linux media closure first.")
    for application in report["applications"]:
        role = application["role"]
        root = raw / "publish" / role
        verify_files(root, application["files"])
        publisher.validate_publish(root, role, rid, deployment == "SelfContained")
    assets = output / "assets"
    assets.mkdir(parents=True)
    for role, product in (("Editor", "Editor"), ("CrossPlatform-Bound", "CrossPlatform-Sample-GameTestCase"),
                          ("CrossPlatform-Player", "CrossPlatform-Sample-Player"),
                          ("CrossPlatform-Developer", "CrossPlatform-Sample-Developer")):
        name = asset_name(product, version, rid, deployment)
        root = output / "staging" / name.removesuffix(".tar.gz").removesuffix(".zip")
        library = root / "lib"
        copy_tree(raw / "publish" / role, library)
        roles = (role, "CLI") if role == "Editor" else (role,)
        if role == "Editor":
            copy_tree(raw / "publish/CLI", library)
            if (library / "Skills").exists():
                shutil.move(str(library / "Skills"), root / "Skills")
            for filename in ("compose.sh", "compose.ps1", "build-compose.sh", "build-compose.ps1", "bind-game.ps1"):
                copy_tree_file = root / "Tools" / filename
                copy_tree_file.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(REPO / "scripts/distribution" / filename, copy_tree_file)
                copy_tree_file.chmod(0o755 if filename.endswith(".sh") else 0o644)
        for current in roles:
            assembly = Path(publisher.PROJECTS[current.split("-")[0]]).stem
            executable = assembly + ("" if linux else ".exe")
            publisher.check_binary(library / executable, rid)
            if linux:
                (library / executable).chmod(0o755)
                (root / executable).write_text(
                    '#!/bin/sh\nset -eu\napp_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\n'
                    'export LD_LIBRARY_PATH="$app_root/lib/native:$app_root/lib/native/vlc:$app_root/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"\n'
                    'export VLC_PLUGIN_PATH="$app_root/lib/native/vlc/plugins"\n'
                    'if [ -e /dev/dxg ] && [ -z "${GALLIUM_DRIVER:-}" ]; then export GALLIUM_DRIVER=d3d12; fi\n'
                    f'exec "$app_root/lib/{executable}" "$@"\n', encoding="utf-8", newline="\n")
                (root / executable).chmod(0o755)
            else:
                host = raw / "apphosts" / (current + ".exe")
                publisher.check_binary(host, rid)
                if ("lib/" + assembly + ".dll").encode() not in host.read_bytes():
                    raise ValueError("Packaged apphost does not target lib/.")
                shutil.copyfile(host, root / executable)
        if linux:
            copy_tree(media / "native", library / "native")
            copy_tree(media / "licenses", root / "docs/licenses")
            shutil.copyfile(media / "media-manifest.json", root / "docs/native-media.json")
            linux_spec = importlib.util.spec_from_file_location("linux_package", REPO / "scripts/release/package-linux.py")
            linux_package = importlib.util.module_from_spec(linux_spec)
            linux_spec.loader.exec_module(linux_package)
            linux_package.verify_native(root)
        (root / "docs").mkdir(exist_ok=True)
        shutil.copyfile(REPO / "LICENSE", root / "docs/LICENSE")
        if role == "CrossPlatform-Bound":
            for entry in report["content"]["contractFiles"]:
                if Path(entry["path"]).name != entry["path"] or digest(library / entry["path"]) != entry["sha256"]:
                    raise ValueError("Game/runtime contract identity differs.")
            copy_tree(raw / "content", root / "pak")
        for directory in ("I18n", "ResourcePatches", "StoryPatches"):
            (root / "pak" / directory).mkdir(parents=True, exist_ok=True)
        entry = Path(publisher.PROJECTS[role.split("-")[0]]).stem + ("" if linux else ".exe")
        write(root / "docs/application-layout.json", {"schemaVersion": 1, "runtimeIdentifier": rid,
              "entryPoint": entry, "managedDirectory": "lib", "contentDirectory": "pak",
              "contractsDirectory": "lib/DefaultContracts" if role == "Editor" else "lib"})
        (root / "docs/README.txt").write_text(
            f"GalNet {product} {version} ({rid}, {deployment})\nRun ../{entry}; keep the entire directory together.\n"
            + ("Requires the x64 .NET 10 Runtime (latest servicing patch), not the SDK.\n" if deployment == "FrameworkDependent"
               else "Includes .NET; no SDK/runtime installation required.\n")
            + ("Ubuntu 24.04 x64 baseline. Host glibc, ICU, display/graphics drivers, fonts and audio service are required.\n"
               "Private VLC dependencies, source provenance and copyright notices are in lib/native and docs/.\n" if linux else "")
            + ("GameTestCase is included; debug and launch arguments are disabled; the player is bound to its project identity.\n"
               if role == "CrossPlatform-Bound" else "")
            + ("No game included. Launch with <content-directory> [--profile <directory>]; debug tools are enabled.\n"
               if role == "CrossPlatform-Developer" else "")
            + ("No game included. Launch with <content-directory> [--profile <directory>]; debug tools are disabled.\n"
               if role == "CrossPlatform-Player" else ""), encoding="utf-8")
        write(root / "docs/release-manifest.json", {"schemaVersion": 1, "version": version,
              "sourceCommit": report["sourceCommit"], "runtime": rid, "package": product,
              "selfContained": deployment == "SelfContained", "readyToRun": False,
              "boundProjectId": report["boundProjectId"] if role == "CrossPlatform-Bound" else None,
              "sampleFlavor": role.partition("-")[2] or None, "files": inventory(root)})
        archive(root, assets / name, linux)
    if deployment == "SelfContained":
        with zipfile.ZipFile(assets / f"GalNet-CrossPlatform-Sample-Contracts-{version}-{rid}.zip", "w", zipfile.ZIP_DEFLATED) as zipped:
            for name in CONTRACTS:
                zipped.write(raw / "publish/CrossPlatform-Bound" / name, name)
            zipped.write(REPO / "LICENSE", "LICENSE")
    write(output / "bundle.json", {"passed": True, "version": version, "sourceCommit": report["sourceCommit"],
          "target": rid, "deployment": deployment, "files": inventory(assets)})


def android(raw, output, version, commit):
    report = read(raw / "android-publish.json")
    verification = read(raw / "android-package-verification.json")
    if not report.get("passed") or not report.get("monoAot") or report.get("gameDebugging") or not report.get("boundProjectId"):
        raise ValueError("A successful bound, non-debug Mono AOT Android publish is required.")
    # verify-android-package.py raises on failure; preserve its full evidence with the bundle.
    if verification.get("apkSha256") != digest(Path(report["apk"])) or verification.get("contentSha256") != digest(Path(report["content"])):
        raise ValueError("Missing Android package evidence.")
    assets = output / "assets"
    assets.mkdir(parents=True)
    shutil.copyfile(Path(report["apk"]), assets / f"GalNet-CrossPlatform-Sample-GameTestCase-{version}-android-arm64.apk")
    write(output / "bundle.json", {"passed": True, "version": version, "sourceCommit": commit,
          "target": "android-arm64", "deployment": "MonoAOT", "files": inventory(assets)})


def seal(root, inputs, version, commit, tag):
    index = read(root / "release-index.json")
    if index["version"] != version or index["sourceCommit"] != commit or index["tag"] != tag or not index["packageValidation"]["passed"]:
        raise ValueError("NuGet source/version/validation does not match the candidate.")
    verify_files(root, index["files"])
    assets = root / "assets"
    assets.mkdir(exist_ok=True)
    bundles = [read(path) for path in sorted(inputs.glob("*/bundle.json"))]
    keys = {(bundle["target"], bundle["deployment"]) for bundle in bundles}
    expected = {(rid, deployment) for rid in ("win-x64", "linux-x64")
                for deployment in ("SelfContained", "FrameworkDependent")} | {("android-arm64", "MonoAOT")}
    if len(bundles) != 5 or keys != expected:
        raise ValueError("Incomplete platform/deployment matrix.")
    for path in sorted(inputs.glob("*/bundle.json")):
        bundle = read(path)
        if not bundle["passed"] or bundle["version"] != version or bundle["sourceCommit"] != commit:
            raise ValueError("Platform source/version/validation does not match the candidate.")
        verify_files(path.parent / "assets", bundle["files"])
        for item in bundle["files"]:
            if Path(item["path"]).name != item["path"] or (assets / item["path"]).exists():
                raise ValueError("Invalid or duplicate platform asset.")
            shutil.copyfile(path.parent / "assets" / item["path"], assets / item["path"])
    if {path.name for path in assets.iterdir()} != expected_assets(version):
        raise ValueError("Unexpected cross-platform release asset set.")
    index.update(schemaVersion=2, runtime="cross-platform", platforms=sorted(keys), files=inventory(root),
                 sampleModes=["Developer", "Player", "Bound"], sampleFlavor=None)
    index["files"] = [item for item in index["files"] if item["path"] not in ("release-index.json", "SHA256SUMS")]
    write(root / "release-index.json", index)
    (root / "SHA256SUMS").write_text("".join(f"{item['sha256']}  {item['path']}\n" for item in index["files"]), encoding="ascii")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    desktop_parser = commands.add_parser("desktop")
    desktop_parser.add_argument("--raw", type=Path, required=True)
    desktop_parser.add_argument("--output", type=Path, required=True)
    desktop_parser.add_argument("--media", type=Path)
    android_parser = commands.add_parser("android")
    android_parser.add_argument("--raw", type=Path, required=True)
    android_parser.add_argument("--output", type=Path, required=True)
    android_parser.add_argument("--version", required=True)
    android_parser.add_argument("--commit", required=True)
    seal_parser = commands.add_parser("seal")
    seal_parser.add_argument("--root", type=Path, required=True)
    seal_parser.add_argument("--inputs", type=Path, required=True)
    seal_parser.add_argument("--version", required=True)
    seal_parser.add_argument("--commit", required=True)
    seal_parser.add_argument("--tag", default="")
    arguments = vars(parser.parse_args())
    command = arguments.pop("command")
    globals()[command](**arguments)
