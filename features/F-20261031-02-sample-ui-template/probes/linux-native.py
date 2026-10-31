"""Run the independently packaged, customized template on an actual Linux display."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--environment', type=Path, required=True)
    parser.add_argument('--consumer', type=Path, required=True)
    parser.add_argument('--content', type=Path, required=True, help='Content exported with the frozen consumer contracts')
    args = parser.parse_args()
    settings = json.loads(args.environment.read_text())
    work = Path(settings['root']).resolve()
    if work.parent != (Path.home() / 'work').resolve() or not work.name.startswith('galnet-sample-ui-p6-'):
        raise ValueError('Unexpected workspace')
    root = work / ('consumer-frozen-' + uuid.uuid4().hex)
    root.mkdir()
    for name in ['Directory.Build.props', 'Directory.Packages.props', 'global.json']:
        shutil.copyfile(args.consumer / name, root / name)
    for name in ['GalNet.Sample.CrossPlatform', 'GalNet.Sample.Contracts', 'NativeFlow']:
        shutil.copytree(args.consumer / name, root / name, ignore=shutil.ignore_patterns('bin', 'obj'))
    shutil.copyfile(Path(__file__).parent / 'NativeFlow.cs', root / 'NativeFlow/Program.cs')
    # Treat the project contract as a delivered artifact shared with the content exporter.
    # Recompiling it on another OS is not a promise of strict byte identity.
    frozen = root / 'Contracts'
    frozen.mkdir()
    shutil.copyfile(args.consumer / 'NativeFlow/bin/Release/net10.0/GalNet.Project.Contracts.dll',
                    frozen / 'GalNet.Project.Contracts.dll')
    app_project = root / 'GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.csproj'
    tree = ET.parse(app_project)
    for group in tree.getroot().findall('ItemGroup'):
        for reference in list(group.findall('ProjectReference')):
            if 'GalNet.Sample.Contracts' in reference.get('Include', ''):
                group.remove(reference)
                artifact = ET.SubElement(group, 'Reference', Include='GalNet.Project.Contracts')
                ET.SubElement(artifact, 'HintPath').text = '../Contracts/GalNet.Project.Contracts.dll'
    tree.write(app_project, encoding='unicode')
    # Feed and package cache are shared read-only inputs to a fresh native build.
    (root / 'NuGet.Config').write_text('''<configuration><packageSources><clear/>
<add key="local" value="/mnt/g/program/GalDotNet/.artifacts/sample-ui-template/p6/packages"/>
<add key="cache" value="/mnt/c/Users/Starry/.nuget/packages"/>
<add key="nuget" value="https://api.nuget.org/v3/index.json"/>
</packageSources></configuration>''')
    environment = dict(os.environ, DOTNET_ROOT=str(Path(settings['sdk']).parent),
                       DOTNET_CLI_HOME=str(work / 'home'), NUGET_PACKAGES='/mnt/c/Users/Starry/.nuget/packages',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', AVALONIA_TELEMETRY_OPTOUT='1',
                       LD_LIBRARY_PATH=str(work / 'media/native'),
                       VLC_PLUGIN_PATH=str(work / 'media/native/vlc/plugins'))
    if Path('/dev/dxg').exists():
        environment.setdefault('GALLIUM_DRIVER', 'd3d12')
    evidence = args.environment.parent
    steps = []

    def run(name, command, timeout=900):
        print(name, flush=True)
        log = evidence / ('linux-native-' + name + '.log')
        with log.open('w') as output:
            result = subprocess.run(list(map(str, command)), cwd=root, env=environment,
                                    stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
        steps.append(dict(name=name, exitCode=result.returncode, log=str(log)))
        (evidence / 'linux-native-steps.json').write_text(json.dumps(steps, indent=2))
        if result.returncode:
            raise RuntimeError(str(log))

    project = root / 'NativeFlow/NativeFlow.csproj'
    run('build', [settings['sdk'], 'build', project, '-c', 'Release', '-p:NuGetAudit=false'])
    binary = root / 'NativeFlow/bin/Release/net10.0'
    shutil.copyfile(work / 'libgalnet_miniaudio.so', binary / 'libgalnet_miniaudio.so')
    content = args.content.resolve()
    output = work / ('native-linux-' + uuid.uuid4().hex)
    run('window', [settings['sdk'], binary / 'NativeFlow.dll', content, output], timeout=180)
    report = json.loads((output / 'native-flow.json').read_text())
    if not report['passed'] or report['headless']:
        raise RuntimeError('Native window flow failed')
    report['sourceCommit'] = settings['sourceCommit']
    report['contractDeployment'] = 'Frozen Windows consumer contract + matching exported content; Linux application compiled natively'
    report['environment'] = 'Ubuntu 24.04.3 / WSLg; not a native Linux desktop distribution test'
    (evidence / 'native-linux.json').write_text(json.dumps(report, indent=2))
    for name in ['story-before-save.png', 'story-after-reopen.png']:
        shutil.copyfile(output / name, evidence / ('linux-' + name))


if __name__ == '__main__':
    main()
