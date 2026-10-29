"""Verify the published GameHost SDL adapter, private deployment and missing-native fallback."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Verification output must be empty.")
    parent = output.parent if platform.system() == "Windows" else Path(tempfile.gettempdir()).resolve()
    runs = []
    with tempfile.TemporaryDirectory(prefix="galnet sdl3 host ", dir=parent) as temporary:
        work = Path(temporary)
        if work.resolve().parent != parent or not work.name.startswith("galnet sdl3 host "):
            raise ValueError("Unexpected host probe temporary directory.")
        shutil.copytree(args.app, work / "app")
        executable = work / "app" / ("GalNet.Sdl3.HostProbe.exe" if platform.system() == "Windows" else "GalNet.Sdl3.HostProbe")
        executable.chmod(0o755)
        environment = dict(os.environ, DOTNET_ROOT=str(work / "no-sdk"), DOTNET_MULTILEVEL_LOOKUP="0")
        if platform.system() == "Linux":
            environment.setdefault("GALLIUM_DRIVER", "d3d12")
        for license_name in ("SDL-LICENSE.txt", "SDL3-CS-LICENSE.txt"):
            if not (work / "app/ThirdParty/SDL3" / license_name).is_file():
                raise RuntimeError("Missing SDL redistribution notice: " + license_name)
        inventory = json.loads((work / "app/ThirdParty/SDL3/native-runtime.json").read_text())
        rid = "win-x64" if platform.system() == "Windows" else "linux-x64"
        asset = inventory["assets"][rid]
        native_file = work / "app" / asset["file"]
        if hashlib.sha256(native_file.read_bytes()).hexdigest() != asset["sha256"]:
            raise RuntimeError("Published SDL does not match the pinned native inventory.")
        for index in range(4):
            extra = []
            if index == 3:
                filename = "SDL3.dll" if platform.system() == "Windows" else "libSDL3.so.0"
                for native in (work / "app").rglob(filename):
                    native.rename(native.with_name(native.name + ".absent"))
                extra = ["--expect-unavailable"]
            result_path = output / f"run-{index}.json"
            with (output / f"run-{index}.log").open("w", encoding="utf-8") as log:
                process = subprocess.run([str(executable), str(result_path), *extra], env=environment,
                                         stdout=log, stderr=subprocess.STDOUT, timeout=45)
            result = json.loads(result_path.read_text(encoding="utf-8"))
            runs.append({"exitCode": process.returncode, "result": result_path.name,
                         "passed": result["passed"], "missingPrivateNative": index == 3})
            if process.returncode != 0 or not result["passed"]:
                raise RuntimeError(f"Host probe failed: {result_path}")
        with (work / "app/GalNet.Sdl3.HostProbe.dll").open("rb") as dll:
            application_hash = hashlib.file_digest(dll, "sha256").hexdigest()
        record = {"runs": runs, "applicationSha256": application_hash,
                  "selfContained": True, "temporaryCopy": str(work), "cleaned": True,
                  "physicalControllerTested": False}
    (output / "verification.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record, indent=2))


if __name__ == "__main__":
    main()
