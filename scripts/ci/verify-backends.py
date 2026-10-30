"""Run portable backend regressions and a real Headless process with explicit evidence."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import xml.etree.ElementTree as ET

from process_steps import run


SUITES = ("GalNet.Core.Tests", "GalNet.Assets.Tests", "GalNet.Runtime.Tests",
          "GalNet.GameHost.Tests", "GalNet.Storage.FileSystem.Tests",
          "GalNet.Media.Audio.Tests", "GalNet.Architecture.Tests")
HEADLESS = "src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj"


def read_test_counts(path):
    root = ET.parse(path).getroot()
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    summary = root.find("t:ResultSummary", namespace)
    counters = summary.find("t:Counters", namespace) if summary is not None else None
    if counters is None:
        raise ValueError(f"Missing TRX counters: {path}")
    counts = {key: int(counters.get(key, "0")) for key in ("total", "executed", "passed", "failed", "notExecuted")}
    # NUnit Explicit cases appear in total/results but may not increment notExecuted.
    counts["notRun"] = counts["total"] - counts["executed"]
    if (summary.get("outcome") != "Completed" or counts["executed"] <= 0
            or counts["passed"] != counts["executed"] or counts["failed"]
            or any(value < 0 for value in counts.values())):
        raise ValueError(f"Tests did not complete successfully: {path}: {counts}")
    return counts


def verify(repo, output, native=None, dotnet="dotnet", restore=True):
    repo, output = repo.resolve(), output.resolve()
    if output == repo or output in repo.parents:
        raise ValueError("Output cannot contain the repository.")
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Use an empty output directory; existing evidence is preserved.")
    logs, results = output / "logs", output / "results"
    logs.mkdir()
    results.mkdir()
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                       AVALONIA_TELEMETRY_OPTOUT="1")
    report = {"schemaVersion": 1, "passed": False, "host": platform.platform(),
              "configuration": "Release", "restore": "locked" if restore else "not_run",
              "steps": [], "checks": [{"name": name, "status": "not_run"} for name in (*SUITES, "Headless")],
              "physicalAudioDeviceTested": False, "guiRuntimeTested": False, "nativeAotTested": False}
    try:
        report["sourceCommit"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
        pinned = json.loads((repo / "global.json").read_text())["sdk"]["version"]
        run([dotnet, "--version"], repo, environment, logs / "sdk.log", report["steps"])
        actual = (logs / "sdk.log").read_text(encoding="utf-8-sig").strip()
        if actual != pinned:
            raise ValueError(f"SDK must be exactly {pinned}; got {actual}")
        report["sdkVersion"] = actual
        if native is None:
            build = output / "native"
            run(["cmake", "-S", repo / "src/Infrastructure/GalNet.Media.Miniaudio/native",
                 "-B", build, "-DCMAKE_BUILD_TYPE=Release"],
                repo, environment, logs / "native-configure.log", report["steps"])
            run(["cmake", "--build", build, "--config", "Release"],
                repo, environment, logs / "native-build.log", report["steps"])
            run(["cmake", "--install", build, "--config", "Release", "--prefix", build / "install"],
                repo, environment, logs / "native-install.log", report["steps"])
            library = {"Windows": "galnet_miniaudio.dll", "Linux": "libgalnet_miniaudio.so",
                       "Darwin": "libgalnet_miniaudio.dylib"}[platform.system()]
            native = build / "install" / library
        native = native.resolve()
        if not native.is_file():
            raise FileNotFoundError(f"Required native audio library is missing: {native}")
        with native.open("rb") as stream:
            report["nativeAudio"] = {"path": str(native), "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}
        # MSBuild treats these characters specially even when passed as one argv item.
        native_property = "".join(f"%{ord(char):02X}" if char in "%$@();?*'," else char for char in str(native))
        common = ["-p:Configuration=Release", "-p:GalNetMiniaudioNativePath=" + native_property,
                  "-p:GalNetBuildMiniaudio=false", "-p:UseSharedCompilation=false", "-m:1", "-nr:false"]
        for check in report["checks"]:
            name = check["name"]
            project = HEADLESS if name == "Headless" else f"test/{name}/{name}.csproj"
            check["status"] = "running"
            try:
                if restore:
                    run([dotnet, "restore", project, "--locked-mode", *common],
                        repo, environment, logs / (name + "-restore.log"), report["steps"])
                if name == "Headless":
                    run([dotnet, "build", project, "--no-restore", *common,
                         "-p:JsonSerializerIsReflectionEnabledByDefault=false"],
                        repo, environment, logs / (name + "-build.log"), report["steps"])
                    assembly = repo / Path(HEADLESS).parent / "bin/Release/net10.0/GalNet.Sample.Headless.dll"
                    log = logs / "Headless-run.log"
                    run([dotnet, assembly, "--aot-self-test"], repo, environment, log, report["steps"], timeout=90)
                    if "AOT_ACCEPTANCE_OK" not in log.read_text(encoding="utf-8-sig"):
                        raise ValueError("Headless exited without the acceptance marker.")
                    check["acceptance"] = "managed engine, packaged resources, persistence, normal exit"
                else:
                    run([dotnet, "test", project, "--no-restore", *common, "--logger",
                         f"trx;LogFileName={name}.trx", "--results-directory", results],
                        repo, environment, logs / (name + "-test.log"), report["steps"])
                    check["tests"] = read_test_counts(results / (name + ".trx"))
                check["status"] = "passed"
            except Exception as error:
                check.update(status="failed", error=str(error))
                # Independent suites still produce evidence after another suite fails.
            except KeyboardInterrupt:
                check.update(status="failed", error="Interrupted")
                raise
        if any(check["status"] != "passed" for check in report["checks"]):
            raise RuntimeError("One or more backend checks failed; see backend-results.json.")
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        raise
    except KeyboardInterrupt:
        report["error"] = "Interrupted"
        raise
    finally:
        (output / "backend-results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--native-audio", type=Path, help="Use an existing library; otherwise build with CMake.")
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--no-restore", action="store_true", help="Local check only; CI uses locked restores.")
    args = parser.parse_args()
    verify(args.repository, args.output, args.native_audio, args.dotnet, not args.no_restore)
    print("Backend acceptance passed: " + str(args.output.resolve()), flush=True)


if __name__ == "__main__":
    main()
