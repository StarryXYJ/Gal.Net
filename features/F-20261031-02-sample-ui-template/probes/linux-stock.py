"""Exercise both untouched native Linux published Sample flavors without SDK discovery."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--environment', type=Path, required=True)
    args = parser.parse_args()
    settings = json.loads(args.environment.read_text())
    work = Path(settings['root']).resolve()
    if work.parent != (Path.home() / 'work').resolve() or not work.name.startswith('galnet-sample-ui-p6-'):
        raise ValueError('Unexpected workspace')
    run_root = work / ('stock-' + uuid.uuid4().hex)
    author = run_root / 'TemplateStock'
    author.mkdir(parents=True)
    for name in ['Assets', 'Graph', 'I18n']:
        shutil.copytree(args.repository / 'GameTestCase' / name, author / name)
    shutil.copyfile(args.repository / 'GameTestCase/settings.json', author / 'settings.json')
    publish = work / 'publish/publish'
    (author / 'Contracts').mkdir()
    for name in ['GalNet.Core.dll', 'GalNet.Contracts.Builtins.dll', 'GalNet.Project.Contracts.dll']:
        shutil.copyfile(publish / 'CrossPlatform-Player' / name, author / 'Contracts' / name)
    environment = dict(os.environ, DOTNET_ROOT=str(Path(settings['sdk']).parent))
    evidence = args.environment.parent
    log = evidence / 'linux-stock-export.log'
    content = run_root / 'content'
    with log.open('w') as output:
        subprocess.run([str(publish / 'CLI/GalNet.Editor.Headless'), 'project', 'export-directory', str(author),
                        '--output', str(content)], env=environment, stdout=output, stderr=subprocess.STDOUT, check=True)
    for flavor in ['Player', 'Developer']:
        application = publish / ('CrossPlatform-' + flavor)
        shutil.copytree(content, application / 'pak')
        output = evidence / ('linux-stock-' + flavor.lower())
        command = ['python3', str(args.repository / 'features/F-20261029-05-cross-platform-applications/probes/source-compose/verify-linux-window.py'),
                   '--application', str(application), '--output', str(output), '--raw-publish',
                   '--entry', 'GalNet.Sample.CrossPlatform.Desktop', '--media-directory', str(work / 'media/native')]
        if flavor == 'Player':
            command += ['--player-project-id', author.name]
        subprocess.run(command, env=environment, check=True, timeout=90)


if __name__ == '__main__':
    main()
