"""Install a template package, instantiate it, and build its desktop consumer."""
import argparse
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
from xml.sax.saxutils import quoteattr


def run(*args):
    subprocess.run(args, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--feed', type=Path, required=True)
    parser.add_argument('--work-directory', type=Path, required=True)
    args = parser.parse_args()
    work = args.work_directory.resolve()
    work.mkdir(parents=True, exist_ok=False)
    hive = str(work / 'hive')
    run('dotnet', 'new', 'install', str(args.package.resolve()), '--debug:custom-hive', hive)
    game = work / 'game'
    title = '星空 & "梦" \\旅'
    run('dotnet', 'new', 'galnet-game', '-n', 'MyGame', '--title', title,
        '-o', str(game), '--debug:custom-hive', hive)
    app = game / 'src/Samples/MyGame'
    assert (game / 'MyGame.slnx').is_file()
    solution = ET.parse(game / 'MyGame.slnx')
    paths = [p.attrib['Path'] for p in solution.findall('Project')]
    assert len(paths) == 6 and all((game / p).is_file() for p in paths)
    for file in (app / 'MyGame/Localization').glob('*.json'):
        assert json.loads(file.read_text(encoding='utf-8-sig'))['App.Title'] == title
    manifest = ET.parse(app / 'MyGame.Android/Properties/AndroidManifest.xml')
    assert manifest.find('application').attrib['{http://schemas.android.com/apk/res/android}label'] == title
    plist = ET.parse(app / 'MyGame.iOS/Info.plist').getroot().find('dict')
    assert list(plist)[1].text == title
    assert 'GalNet.Project.Contracts' in (game / 'src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj').read_text(encoding='utf-8-sig')
    default = work / 'default'
    run('dotnet', 'new', 'galnet-game', '-n', 'AnotherGame', '-o', str(default), '--debug:custom-hive', hive)
    default_title = json.loads((default / 'src/Samples/AnotherGame/AnotherGame/Localization/en-US.json').read_text(encoding='utf-8-sig'))['App.Title']
    assert default_title == 'AnotherGame', default_title
    config = work / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="local" value='
                      + quoteattr(str(args.feed.resolve())) + '/><add key="nuget" value="https://api.nuget.org/v3/index.json" />'
                      '</packageSources></configuration>', encoding='utf-8')
    project = app / 'MyGame.Desktop/MyGame.Desktop.csproj'
    run('dotnet', 'restore', str(project), '--configfile', str(config), '-p:NuGetAudit=false')
    run('dotnet', 'build', str(project), '--no-restore', '-v:q')
    print('Template installation, generation, title, solution and Desktop build passed.')


if __name__ == '__main__':
    main()
