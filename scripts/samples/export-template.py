"""Copy the one maintained Sample and its build attachments; do not rewrite project sources."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repository, output = args.repository.resolve(), args.output.resolve()
    recipe_path = repository / 'src/Samples/GalNet.Sample.CrossPlatform/Template/files.json'
    recipe = json.loads(recipe_path.read_text(encoding='utf-8-sig'))
    sources = [repository / path for path in recipe['trees']]
    if output.exists() or output == repository or repository.is_relative_to(output):
        parser.error('Use a new output directory that does not contain the repository.')
    if any(output.is_relative_to(source) for source in sources):
        parser.error('The destination cannot be inside an input tree.')
    planned = {}

    def add(source, target):
        if source.is_symlink() or not source.is_file():
            raise ValueError('Expected a regular source file: ' + str(source))
        if not source.resolve().is_relative_to(repository) or target.is_absolute() or '..' in target.parts:
            raise ValueError('A copy path escaped the declared roots.')
        if target.as_posix() in planned:
            raise ValueError('Duplicate destination: ' + str(target))
        planned[target.as_posix()] = source

    for relative in recipe['trees']:
        source = repository / relative
        if not source.is_dir():
            raise FileNotFoundError(source)
        for path in sorted(source.rglob('*')):
            parts = path.relative_to(source).parts
            if any(part in ('bin', 'obj', '.git', '.vs') for part in parts) or path.is_relative_to(recipe_path.parent):
                continue
            if path.relative_to(repository).as_posix() in recipe.get('exclude', []):
                continue
            if path.is_file() and not path.name.startswith('packages.lock') and path.suffix != '.user':
                add(path, path.relative_to(repository))
    for source, target in recipe['files'].items():
        add(repository / source, Path(target))
    # Validate every input before creating anything; inherit the destination parent's ACL.
    output.mkdir(parents=True)
    files = []
    for relative, source in planned.items():
        data = source.read_bytes()
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        files.append(dict(path=relative, source=source.relative_to(repository).as_posix(),
                          sha256=hashlib.sha256(data).hexdigest()))
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repository, text=True).strip()
    dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=repository, text=True, encoding='utf-8').strip())
    (output / 'template-manifest.json').write_text(json.dumps(dict(schemaVersion=1, sourceCommit=revision,
        sourceWorktreeDirty=dirty, files=files), indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(output=str(output), fileCount=len(files), sourceCommit=revision,
                          sourceWorktreeDirty=dirty), indent=2))


if __name__ == '__main__':
    main()
