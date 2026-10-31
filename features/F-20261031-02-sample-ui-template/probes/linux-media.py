"""Run real LibVLC Gallery regressions; headless controls, native media decoding."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--environment', type=Path, required=True)
    args = parser.parse_args()
    settings = json.loads(args.environment.read_text())
    work = Path(settings['root']).resolve()
    if work.parent != (Path.home() / 'work').resolve() or not work.name.startswith('galnet-sample-ui-p6-'):
        raise ValueError('Unexpected workspace')
    source = Path(settings['source'])
    environment = dict(os.environ, DOTNET_ROOT=str(Path(settings['sdk']).parent),
                       DOTNET_CLI_HOME=str(work / 'home'), NUGET_PACKAGES='/mnt/c/Users/Starry/.nuget/packages',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1',
                       LD_LIBRARY_PATH=str(work / 'media/native') + ':' + str(work),
                       VLC_PLUGIN_PATH=str(work / 'media/native/vlc/plugins'))
    project = source / 'test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj'
    evidence = args.environment.parent
    filters = '|'.join('FullyQualifiedName~' + name for name in [
        'VideoGalleryCanPauseSeekResumeAndReleaseBothSampleVideos',
        'MusicGalleryRetainsEachTrackPositionAndCanStartBySeeking',
        'MusicDurationParsingCanBeCanceledDuringGalleryDisposal'])
    commands = [
        ('build', [settings['sdk'], 'build', str(project), '-c', 'Release', '-p:GalNetBuildMiniaudio=false']),
        ('test', [settings['sdk'], 'test', str(project), '-c', 'Release', '--no-build', '--no-restore',
                  '--filter', filters, '--logger', 'trx;LogFileName=linux-media.trx', '--results-directory', str(evidence)])]
    for name, command in commands:
        print(name, flush=True)
        log = evidence / ('linux-media-' + name + '.log')
        with log.open('w') as output:
            result = subprocess.run(command, cwd=source, env=environment, stdout=output,
                                    stderr=subprocess.STDOUT, timeout=900)
        if result.returncode:
            raise RuntimeError(str(log))


if __name__ == '__main__':
    main()
