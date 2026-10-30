"""Exercise the native Linux SDK -> export -> build-compose protocol, including an external host."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import time
from xml.sax.saxutils import escape


CONTRACTS = ("GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "GalNet.Project.Contracts.dll")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--published-sample", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--native-audio", type=Path, required=True)
    parser.add_argument("--media-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    if os.name == "nt":
        parser.error("Use a native Linux SDK host.")
    source, output = args.source.resolve(), args.output.resolve()
    if output == source or output in source.parents:
        parser.error("Output cannot contain the source.")
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("Use an empty verification output directory.")
    environment = dict(os.environ, PATH=str(args.dotnet.resolve().parent) + os.pathsep + os.environ.get("PATH", ""),
                       DOTNET_ROOT=str(args.dotnet.resolve().parent), AVALONIA_TELEMETRY_OPTOUT="1")
    steps, cases = [], []
    report = {"passed": False, "sdk": None, "source": str(source), "version": args.version,
              "steps": steps, "cases": cases, "guiTested": False, "minimumOsTested": False,
              "crossHostContractInterchangeabilityTested": False}

    def run(name, arguments, expected=0, json_output=True, sdk=True):
        log = output / (name + ".log")
        selected = environment.copy()
        if not sdk:
            selected.update(PATH="/usr/bin:/bin", DOTNET_ROOT=str(output / "no-sdk"), DOTNET_MULTILEVEL_LOOKUP="0")
            selected.pop("DOTNET_HOST_PATH", None)
            selected.pop("DOTNET_ROOT_X64", None)
        start = time.monotonic()
        with log.open("w", encoding="utf-8") as stream:
            process = subprocess.run(list(map(str, arguments)), cwd=source, env=selected,
                                     stdout=stream, stderr=subprocess.STDOUT, timeout=900)
        steps.append({"name": name, "exitCode": process.returncode, "log": str(log),
                      "seconds": time.monotonic() - start, "sdkDiscoverable": sdk})
        print(f"{name}: {process.returncode}", flush=True)
        if process.returncode != expected:
            raise RuntimeError(f"Unexpected exit {process.returncode}: {log}")
        data = json.loads(log.read_text()) if json_output else log.read_text().strip()
        if json_output and expected == 0 and data.get("success") is False:
            raise RuntimeError(f"Operation reported failure: {log}")
        return data

    def unpack(archive, name):
        target = output / (name + " unpacked with spaces")
        target.mkdir()
        with tarfile.open(archive) as package:
            package.extractall(target, filter="data")
        roots = list(target.iterdir())
        if len(roots) != 1 or not roots[0].is_dir():
            raise RuntimeError("Expected one archive root.")
        root = roots[0]
        layout = json.loads((root / "docs/application-layout.json").read_text())
        manifest = json.loads((root / "docs/release-manifest.json").read_text())
        declared = set()
        for item in manifest["files"]:
            path = root / item["path"]
            if path.stat().st_size != item["size"] or digest(path) != item["sha256"].lower():
                raise RuntimeError(f"Manifest mismatch: {path}")
            declared.add(item["path"])
        actual = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
        if actual != declared | {"docs/release-manifest.json"}:
            raise RuntimeError("Undeclared or missing distribution payload.")
        for entry in (layout["entryPoint"], manifest["nativeEntryPoint"]):
            if not os.access(root / entry, os.X_OK):
                raise RuntimeError(f"Entry lost executable permissions: {entry}")
        for filename, sha256 in report["contracts"].items():
            if digest(root / layout["contractsDirectory"] / filename) != sha256:
                raise RuntimeError(f"Contract bytes changed during source composition: {filename}")
        return root, layout, manifest

    try:
        report["sdk"] = run("sdk", [args.dotnet, "--version"], json_output=False)
        pinned = json.loads((source / "global.json").read_text())["sdk"]["version"]
        if report["sdk"] != pinned:
            raise RuntimeError("SDK does not match global.json.")
        contracts = output / "contracts"
        contracts.mkdir()
        for filename in CONTRACTS:
            shutil.copyfile(args.published_sample / filename, contracts / filename)
        report["contracts"] = {name: digest(contracts / name) for name in CONTRACTS}
        author, exported, directory = output / "author with spaces", output / "export", output / "content"
        run("create", [args.cli, "project", "create", author, "--contracts", contracts], sdk=False)
        run("export-archive", [args.cli, "export", author, "--output", exported], sdk=False)
        run("export-directory", [args.cli, "project", "export-directory", author, "--output", directory], sdk=False)
        archive = next(exported.glob("*.galpak"))

        # The external project only implements the explicit layout/content contract;
        # it deliberately does not depend on the Sample executable or its UI.
        custom = output / "external application"
        custom.mkdir()
        for filename in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"):
            shutil.copyfile(source / filename, custom / filename)
        projects = ("src/Shared/GalNet.Core/GalNet.Core.csproj",
                    "src/Shared/GalNet.Contracts.Builtins/GalNet.Contracts.Builtins.csproj",
                    "src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj",
                    "src/Infrastructure/GalNet.Storage.FileSystem/GalNet.Storage.FileSystem.csproj")
        references = "".join(f'<ProjectReference Include="{escape(str(source / path))}" />' for path in projects)
        project = custom / "CustomHost.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                           '<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>'
                           + references + '</ItemGroup></Project>\n')
        profile = custom / "Properties/PublishProfiles/Desktop.pubxml"
        profile.parent.mkdir(parents=True)
        profile.write_text("<Project />\n")
        (custom / "Program.cs").write_text('using GalNet.Storage.FileSystem;\n'
            'var content = InstalledGameDescriptor.Read(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../pak")));\n'
            'Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { content.ProjectId, content.BuildId }));\n')
        # A newly authored external project needs an explicit initial lock graph.
        # The operation under test below must then use that graph in locked mode.
        run("custom-initial-lock", [args.dotnet, "restore", project, "--force-evaluate", "-m:1", "-nr:false",
            "-p:Configuration=Release", "-p:PublishProfile=Desktop", "-p:RuntimeIdentifier=linux-x64",
            "-p:GalNetDesktopPublishTarget=linux-x64", "-p:SelfContained=true", "-p:RestoreLockedMode=false",
            "-p:NuGetAudit=false", "-p:Version=" + args.version], json_output=False)
        sample = source / "src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj"
        for name, app_project, entry, content in (("sample-archive", sample, "GalNet.Sample.Avalonia", archive),
                                                 ("sample-directory", sample, "GalNet.Sample.Avalonia", directory),
                                                 ("custom-directory", project, "CustomHost", directory)):
            package = output / (name + ".tar.gz")
            result = run(name, [args.cli, "build-compose", "--app-project", app_project, "--content", content,
                "--runtime-id", "linux-x64", "--entry-point", entry, "--version", args.version,
                "--sample-flavor", "Developer", "--publish-profile", "Desktop", "--sdk-version", pinned,
                "--miniaudio-native", args.native_audio, "--media-directory", args.media_directory, "--output", package])
            root, layout, manifest = unpack(package, name)
            cases.append({"name": name, "archiveSha256": digest(package), "root": str(root),
                          "filesVerified": len(manifest["files"]), "buildReport": result["data"]})
            if name.startswith("custom"):
                actual = run("custom-start", [root / layout["entryPoint"]], sdk=False)
                if actual["ProjectId"] != manifest["content"]["projectId"] or actual["BuildId"] != manifest["content"]["buildId"]:
                    raise RuntimeError("The external host loaded different content.")
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        (output / "verification.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
