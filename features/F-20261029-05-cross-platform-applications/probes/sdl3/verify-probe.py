"""Run a self-contained native probe from an isolated path, then remove its copy."""
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
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Verification output must be empty.")
    parent = output.parent if platform.system() == "Windows" else Path(tempfile.gettempdir()).resolve()
    runs = []
    with tempfile.TemporaryDirectory(prefix="galnet sdl3 probe ", dir=parent) as temporary:
        work = Path(temporary)
        if work.resolve().parent != parent or not work.name.startswith("galnet sdl3 probe "):
            raise ValueError("Unexpected probe temporary directory.")
        shutil.copytree(args.app, work / "app")
        shutil.copytree(args.native, work / "native")
        executable = work / "app" / ("Sdl3Probe.exe" if platform.system() == "Windows" else "Sdl3Probe")
        executable.chmod(0o755)
        environment = dict(os.environ, DOTNET_ROOT=str(work / "no-sdk"), DOTNET_MULTILEVEL_LOOKUP="0")
        if platform.system() == "Linux":
            environment.setdefault("GALLIUM_DRIVER", "d3d12")
        for index in range(3):
            result_path = output / f"run-{index}.json"
            with (output / f"run-{index}.log").open("w", encoding="utf-8") as log:
                process = subprocess.run([str(executable), str(work / "native/native.json"), str(result_path)],
                                         env=environment, stdout=log, stderr=subprocess.STDOUT, timeout=45)
            result = json.loads(result_path.read_text(encoding="utf-8"))
            runs.append({"exitCode": process.returncode, "result": result_path.name, "passed": result["passed"]})
            if process.returncode != 0 or not result["passed"]:
                raise RuntimeError(f"Probe failed: {result_path}")
        manifest_path = work / "native/native.json"
        metadata = json.loads(manifest_path.read_text())
        metadata["sha256"] = "0" * 64
        manifest_path.write_text(json.dumps(metadata))
        result_path = output / "bad-hash.json"
        with (output / "bad-hash.log").open("w", encoding="utf-8") as log:
            process = subprocess.run([str(executable), str(manifest_path), str(result_path)],
                                     env=environment, stdout=log, stderr=subprocess.STDOUT, timeout=15)
        invalid = json.loads(result_path.read_text())
        if process.returncode != 1 or invalid["passed"] or "hash mismatch" not in invalid["error"]:
            raise RuntimeError("Corrupt native manifest was not rejected.")
        with (work / "app/Sdl3Probe.dll").open("rb") as dll:
            application_hash = hashlib.file_digest(dll, "sha256").hexdigest()
        record = {"runs": runs, "badHashExitCode": process.returncode,
                  "applicationSha256": application_hash, "selfContained": True,
                  "temporaryCopy": str(work), "cleaned": True,
                  "physicalControllerTested": False}
    (output / "verification.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record, indent=2))


if __name__ == "__main__":
    main()
