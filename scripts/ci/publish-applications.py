"""Build actual desktop applications with the pinned SDK and validate raw publish payloads."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import time


PROJECTS = {
    "Editor": "src/Editor/GalNet.Editor/GalNet.Editor.csproj",
    "CLI": "src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj",
    "Sample": "src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj",
    "CrossPlatform": "src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop/GalNet.Sample.CrossPlatform.Desktop.csproj",
}
TARGETS = {
    "win-x64": ("galnet_miniaudio.dll", "SDL3.dll", "coreclr.dll", ".exe"),
    "linux-x64": ("libgalnet_miniaudio.so", "libSDL3.so.0", "libcoreclr.so", ""),
    "osx-x64": ("libgalnet_miniaudio.dylib", "libSDL3.0.dylib", "libcoreclr.dylib", ""),
    "osx-arm64": ("libgalnet_miniaudio.dylib", "libSDL3.0.dylib", "libcoreclr.dylib", ""),
}


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_binary(path, target):
    with path.open("rb") as stream:
        header = stream.read(64)
        if target.startswith("win-"):
            if header[:2] != b"MZ":
                raise ValueError(f"Not a PE image: {path}")
            stream.seek(int.from_bytes(header[60:64], "little"))
            pe = stream.read(6)
            valid = pe == b"PE\0\0\x64\x86"
        elif target == "linux-x64":
            valid = header[:6] == b"\x7fELF\x02\x01" and header[18:20] == b"\x3e\0"
        else:
            cpu = 0x01000007 if target == "osx-x64" else 0x0100000c
            valid = header[:4] == b"\xcf\xfa\xed\xfe" and int.from_bytes(header[4:8], "little") == cpu
        if not valid:
            raise ValueError(f"Binary architecture does not match {target}: {path}")


def msbuild_value(value):
    return "".join(f"%{ord(char):02X}" if char in "%$@();?*'," else char for char in str(value))


def run(command, cwd, environment, log, steps, timeout=1200):
    started = time.monotonic()
    record = {"command": [str(value) for value in command], "log": str(log), "exitCode": None}
    steps.append(record)
    print(log.name, flush=True)
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(record["command"], cwd=cwd, env=environment,
                                   stdout=output, stderr=subprocess.STDOUT,
                                   start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
        try:
            record["exitCode"] = process.wait(timeout=timeout)
        except (KeyboardInterrupt, subprocess.TimeoutExpired):
            # Only the process tree created by this step is terminated.
            if process.poll() is not None:
                pass
            elif os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            record["exitCode"] = process.returncode
            raise
        finally:
            record["seconds"] = time.monotonic() - started
    if record["exitCode"] != 0:
        raise RuntimeError(f"Step failed ({record['exitCode']}): {log}")


def validate_publish(root, role, target):
    kind = role.split("-")[0]
    assembly = Path(PROJECTS[kind]).stem
    entry = root / (assembly + TARGETS[target][3])
    for binary in (entry, root / TARGETS[target][2]):
        if not binary.is_file():
            raise FileNotFoundError(f"Incomplete {role} publish: {binary}")
        check_binary(binary, target)
    for suffix in (".dll", ".deps.json", ".runtimeconfig.json"):
        if not (root / (assembly + suffix)).is_file():
            raise FileNotFoundError(f"Missing application payload: {assembly}{suffix}")
    dependencies = json.loads((root / (assembly + ".deps.json")).read_text())["libraries"]
    if any(name.startswith("GalNet.Media.Miniaudio/") for name in dependencies):
        check_binary(root / TARGETS[target][0], target)
    contracts = root / "DefaultContracts" if kind == "Editor" else root
    contract_names = ["GalNet.Core.dll", "GalNet.Contracts.Builtins.dll"]
    if kind != "CLI":
        contract_names.append("GalNet.Project.Contracts.dll")
    for filename in contract_names:
        if not (contracts / filename).is_file():
            raise FileNotFoundError(f"Incomplete contracts in {role}: {filename}")
    if kind in ("Sample", "CrossPlatform"):
        notices = root / "ThirdParty/SDL3"
        for filename in ("native-runtime.json", "SDL-LICENSE.txt", "SDL3-CS-LICENSE.txt"):
            if not (notices / filename).is_file():
                raise FileNotFoundError(f"Missing SDL notice: {filename}")
        native = root / TARGETS[target][1]
        check_binary(native, target)
        inventory = json.loads((notices / "native-runtime.json").read_text())
        if sha256(native) != inventory["assets"][target]["sha256"]:
            raise ValueError("SDL differs from the pinned native inventory.")
    return {"role": role, "root": str(root), "entry": entry.name,
            "contracts": {filename: sha256(contracts / filename) for filename in contract_names},
            "files": [{"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": sha256(path)}
                      for path in sorted(root.rglob("*")) if path.is_file()]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=TARGETS, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--native-audio", type=Path)
    parser.add_argument("--version", default="0.0.0-dev.1")
    parser.add_argument("--flavors", nargs="+", choices=["Player", "Developer"], default=["Player", "Developer"])
    parser.add_argument("--update-locks", action="store_true", help="Explicit maintainer action; CI always uses locked restores.")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-(?:dev|preview)\.\d+)?", args.version):
        parser.error("Use a release or numbered dev/preview version.")
    repo, output = args.repository.resolve(), args.output.resolve()
    if output == repo or output in repo.parents:
        parser.error("Output cannot contain the repository.")
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("Use an empty output directory; existing files are preserved.")
    logs = output / "logs"
    logs.mkdir()
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                       DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", AVALONIA_TELEMETRY_OPTOUT="1")
    report = {"passed": False, "target": args.target, "version": args.version,
              "rawPublishOnly": True, "runtimeTested": False, "physicalControllerTested": False,
              "privateVideoClosureTested": False, "steps": [], "applications": []}
    try:
        pinned = json.loads((repo / "global.json").read_text())["sdk"]["version"]
        run([args.dotnet, "--version"], repo, environment, logs / "sdk.log", report["steps"])
        actual = [line.strip() for line in (logs / "sdk.log").read_text().splitlines() if re.fullmatch(r"\d+\.\d+\.\d+", line.strip())]
        if actual != [pinned]:
            raise ValueError(f"SDK must be exactly {pinned}; got {actual}")
        report["sdkVersion"] = pinned
        if args.native_audio:
            native = args.native_audio.resolve()
        else:
            host_os = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}[platform.system()]
            host_arch = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
            if args.target != f"{host_os}-{host_arch}":
                raise ValueError("Cross-host publishing requires an explicit target --native-audio library.")
            build = output / "native/build"
            install = output / "native/install"
            run(["cmake", "-S", str(repo / "src/Infrastructure/GalNet.Media.Miniaudio/native"),
                 "-B", str(build), "-DCMAKE_BUILD_TYPE=Release"], repo, environment, logs / "native-configure.log", report["steps"])
            run(["cmake", "--build", str(build), "--config", "Release", "--parallel", "4"],
                repo, environment, logs / "native-build.log", report["steps"])
            run(["cmake", "--install", str(build), "--config", "Release", "--prefix", str(install)],
                repo, environment, logs / "native-install.log", report["steps"])
            native = install / TARGETS[args.target][0]
        check_binary(native, args.target)
        report["nativeAudio"] = {"path": str(native), "sha256": sha256(native)}
        roles = [("Editor", None), ("CLI", None)] + [("Sample-" + flavor, flavor) for flavor in dict.fromkeys(args.flavors)] + [("CrossPlatform", None)]
        for role, flavor in roles:
            kind = role.split("-")[0]
            project = repo / PROJECTS[kind]
            properties = ["-p:Configuration=Release", "-p:PublishProfile=Desktop",
                          "-p:RuntimeIdentifier=" + args.target, "-p:GalNetDesktopPublishTarget=" + args.target,
                          "-p:GalNetMiniaudioNativePath=" + msbuild_value(native), "-p:GalNetBuildMiniaudio=false",
                          "-p:Version=" + args.version, "-p:ContinuousIntegrationBuild=true",
                          "-p:CopyOutputSymbolsToPublishDirectory=false", "-p:UseSharedCompilation=false",
                          "-p:NuGetAudit=false", "-m:1", "-nr:false"]
            if flavor:
                enabled = "true" if flavor == "Developer" else "false"
                properties += ["-p:EnableGameDebugging=" + enabled, "-p:EnableLaunchArguments=" + enabled]
            restore = [args.dotnet, "restore", str(project), *properties]
            restore += ["--force-evaluate", "-p:RestoreLockedMode=false"] if args.update_locks else ["--locked-mode"]
            run(restore, repo, environment, logs / (role + "-restore.log"), report["steps"])
            destination = output / "publish" / role
            run([args.dotnet, "publish", str(project), "-c", "Release", "--no-restore", "-o", str(destination), *properties],
                repo, environment, logs / (role + "-publish.log"), report["steps"])
            report["applications"].append(validate_publish(destination, role, args.target))
        for filename in ("GalNet.Core.dll", "GalNet.Contracts.Builtins.dll"):
            hashes = {application["contracts"][filename] for application in report["applications"]}
            if len(hashes) != 1:
                raise ValueError("Strict shared contract bytes differ between application publishes: " + filename)
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        (output / "application-publish.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Validated raw application publishes: " + str(output), flush=True)


if __name__ == "__main__":
    main()
