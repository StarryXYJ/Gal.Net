"""Build the frozen implementation with the native Linux SDK in an independent checkout."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--environment', type=Path, required=True)
    parser.add_argument('--commit', required=True)
    parser.add_argument('--version', required=True)
    args = parser.parse_args()
    settings = json.loads(args.environment.read_text())
    work = Path(settings['root']).resolve()
    if work.parent != (Path.home() / 'work').resolve() or not work.name.startswith('galnet-sample-ui-p6-'):
        raise ValueError('Unexpected owned workspace')
    source = work / 'source'
    evidence = args.environment.parent
    variables = dict(os.environ, DOTNET_CLI_HOME=str(work / 'home'),
                     NUGET_PACKAGES='/mnt/c/Users/Starry/.nuget/packages',
                     DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                     DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    steps = []

    def run(name, command, cwd=work):
        log = evidence / ('linux-' + name + '.log')
        print(name, flush=True)
        with log.open('w') as output:
            result = subprocess.run(command, cwd=cwd, env=variables, stdout=output, stderr=subprocess.STDOUT)
        steps.append(dict(name=name, command=command, exitCode=result.returncode, log=str(log)))
        (evidence / 'linux-steps.json').write_text(json.dumps(steps, indent=2))
        if result.returncode:
            raise RuntimeError(name + ' failed: ' + str(log))

    run('clone', ['git', 'clone', '--no-hardlinks', str(args.repository.resolve()), str(source)])
    run('checkout', ['git', 'checkout', '--detach', args.commit], source)
    actual = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source, text=True).strip()
    if actual != args.commit:
        raise RuntimeError('Wrong source revision')
    settings['source'] = str(source)
    settings['sourceCommit'] = actual
    args.environment.write_text(json.dumps(settings, indent=2))
    native = work / 'libgalnet_miniaudio.so'
    native_source = source / 'src/Infrastructure/GalNet.Media.Miniaudio/native/galnet_miniaudio.c'
    run('native-audio', ['cc', '-std=c99', '-O2', '-fPIC', '-fvisibility=hidden', '-DMA_NO_NULL',
                         '-shared', str(native_source), '-o', str(native), '-pthread', '-lm', '-ldl'])
    run('publish', ['python3', str(source / 'scripts/ci/publish-applications.py'), '--repository', str(source),
                    '--dotnet', settings['sdk'], '--target', 'linux-x64', '--version', args.version,
                    '--native-audio', str(native), '--flavors', 'Player', 'Developer',
                    '--output', str(work / 'publish')], source)
    report = work / 'publish/application-publish.json'
    if report.exists():
        (evidence / 'linux-application-publish.json').write_bytes(report.read_bytes())


if __name__ == '__main__':
    main()
