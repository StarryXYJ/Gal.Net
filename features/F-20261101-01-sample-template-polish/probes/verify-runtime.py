"""Build/publish the copied Desktop shell and exercise its real native host and process exits."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--consumer', type=Path, required=True)
    parser.add_argument('--producer', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--resume-processes', action='store_true', help='Recheck an existing publish and rerun only its process tests')
    args = parser.parse_args()
    repo, root, output = args.repository.resolve(), args.consumer.resolve(), args.output.resolve()
    if output.exists() and not args.resume_processes:
        raise FileExistsError(output)
    output.mkdir(parents=True, exist_ok=args.resume_processes)
    common = ['-p:GalNetPackageVersion=' + args.version, '-p:Version=' + args.version,
              '-p:ContinuousIntegrationBuild=true', '-p:NuGetAudit=false', '-p:Configuration=Release']
    environment = dict(os.environ, GALNET_TEMPLATE_RUNTIME_ROOT=str(output))
    steps = json.loads((output / 'steps.json').read_text()) if args.resume_processes else []

    def run(name, command, timeout=600):
        print(name, flush=True)
        log = output / (name + '.log')
        with log.open('w', encoding='utf-8') as stream:
            result = subprocess.run(list(map(str, command)), cwd=root, env=environment, stdout=stream,
                                    stderr=subprocess.STDOUT, timeout=timeout)
        steps.append(dict(name=name, command=list(map(str, command)), exitCode=result.returncode, log=str(log)))
        (output / 'steps.json').write_text(json.dumps(steps, indent=2))
        if result.returncode:
            raise RuntimeError(str(log))

    native = root / 'NativeFlow'
    native.mkdir(exist_ok=True)
    shutil.copyfile(repo / 'features/F-20261031-02-sample-ui-template/probes/NativeFlow.cs', native / 'Program.cs')
    (native / 'NativeFlow.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<ItemGroup><ProjectReference Include="../src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.csproj"/>
<PackageReference Include="Avalonia.Desktop"/><PackageReference Include="VideoLAN.LibVLC.Windows" Condition="$([MSBuild]::IsOSPlatform('Windows'))"/></ItemGroup></Project>''')
    if not args.resume_processes:
        run('native-build', ['dotnet', 'build', native / 'NativeFlow.csproj', *common, '-p:EnableGameDebugging=false'])
    binary = native / 'bin/Release/net10.0'
    author = output / 'author/TemplatePolishRuntime'
    author.mkdir(parents=True, exist_ok=args.resume_processes)
    for name in ['Assets', 'Graph', 'I18n']:
        if not args.resume_processes:
            shutil.copytree(repo / 'GameTestCase' / name, author / name)
    shutil.copyfile(repo / 'GameTestCase/settings.json', author / 'settings.json')
    (author / 'Contracts').mkdir(exist_ok=args.resume_processes)
    contracts = ['GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll']
    for name in contracts:
        shutil.copyfile(binary / name, author / 'Contracts' / name)
    content = output / 'content'
    if not args.resume_processes:
        run('export', [args.producer.resolve(), 'project', 'export-directory', author, '--output', content])
        run('native-window', ['dotnet', binary / 'NativeFlow.dll', content, output / 'native-window'], timeout=180)
    report = json.loads((output / 'native-window/native-flow.json').read_text())
    if not report['passed'] or report['headless']:
        raise RuntimeError('Native host did not pass')
    project = root / 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop/GalNet.Sample.CrossPlatform.Desktop.csproj'
    hashes = {name: hashlib.sha256((binary / name).read_bytes()).hexdigest() for name in contracts}
    for flavor in ['Player', 'Developer']:
        properties = [*common, '-p:GalNetPlayerMode=' + flavor, '-p:PublishProfile=Desktop',
                      '-p:GalNetDesktopPublishTarget=win-x64', '-p:RuntimeIdentifier=win-x64']
        destination = output / flavor
        if not args.resume_processes:
            run('publish-' + flavor, ['dotnet', 'publish', project, *properties, '--self-contained', 'true', '-o', destination])
            run('locked-' + flavor, ['dotnet', 'restore', project, *properties, '--locked-mode'])
        for name, expected in hashes.items():
            if hashlib.sha256((destination / name).read_bytes()).hexdigest() != expected:
                raise RuntimeError('Contract bytes changed for ' + flavor + '/' + name)
        if (destination / 'LibVLCSharp.Avalonia.dll').exists():
            raise RuntimeError('Unused video control reached publish output')
        has_debug_ui = (destination / 'GalNet.Avalonia.Debugging.dll').exists()
        if has_debug_ui != (flavor == 'Developer'):
            raise RuntimeError('Incorrect optional debugging closure')
        if not args.resume_processes:
            shutil.copytree(content, destination / 'pak')
    tests = root / 'TemplateProbe'
    shutil.copyfile(repo / 'test/GalNet.IntegrationTests/NativeSampleWindow.cs', tests / 'NativeSampleWindow.cs')
    (tests / 'TemplateDesktopSmoke.cs').write_text('''using System.Diagnostics;
using System.Text.Json;
namespace GalNet.IntegrationTests;
public sealed class TemplateDesktopSmoke
{
    [TestCase("Player")]
    [TestCase("Developer")]
    public async Task PublishedShellStartsAndConfirmsNormalExitWithoutSdkDiscovery(string flavor)
    {
        var root = Environment.GetEnvironmentVariable("GALNET_TEMPLATE_RUNTIME_ROOT")!;
        var profile = Path.Combine(root, "process-" + flavor + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "player-variables.json"), """
        {"player.galnet.startup.launchCount":{"Uid":"11111111111111111111111111111111","Name":"player.galnet.startup.launchCount","Type":1,"Value":1},"player.galnet.settings.locale":{"Uid":"22222222222222222222222222222222","Name":"player.galnet.settings.locale","Type":3,"Value":"en-US"}}
        """);
        var start = new ProcessStartInfo(Path.Combine(root, flavor, "GalNet.Sample.CrossPlatform.Desktop.exe"))
        { WorkingDirectory = profile, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile);
        start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        start.Environment["DOTNET_ROOT"] = Path.Combine(profile, "no-sdk");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            // The copied project's per-launch opening runs before mounting the host UI.
            await Task.Delay(TimeSpan.FromSeconds(21));
            var ready = false;
            for (var i = 0; i < 100 && !process.HasExited; i++)
            {
                ready = Directory.Exists(Path.Combine(profile, "logs")) && Directory.GetFiles(Path.Combine(profile, "logs"), "game-*.log")
                    .Any(IsReadyLog);
                if (ready) break;
                await Task.Delay(100);
            }
            Assert.That(ready, Is.True, "The published shell did not initialize its real content.");
            NativeSampleWindow.Close(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.That(process.ExitCode, Is.Zero);
            using var values = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(profile, "player-variables.json")));
            Assert.That(values.RootElement.GetProperty("player.galnet.startup.launchCount").GetProperty("Value").GetInt32(), Is.EqualTo(2));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree:true); await process.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(profile, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(profile, "stderr.log"), await stderr);
        }
    }
    private static bool IsReadyLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Contains("Session initialized. SaveSlots=");
    }
}''')
    run('process-restore', ['dotnet', 'restore', tests / 'TemplateProbe.csproj', *common,
        '-p:EnableGameDebugging=false'])
    run('published-processes', ['dotnet', 'test', tests / 'TemplateProbe.csproj', *common,
        '-p:EnableGameDebugging=false', '--no-restore', '--filter', 'FullyQualifiedName~TemplateDesktopSmoke',
        '--logger', 'trx;LogFileName=published-processes.trx', '--results-directory', output], timeout=180)
    (output / 'runtime-validation.json').write_text(json.dumps(dict(passed=True, contracts=hashes,
        nativeHost=report, flavors=['Player', 'Developer'], sdkDiscovery=False), indent=2))


if __name__ == '__main__':
    main()
