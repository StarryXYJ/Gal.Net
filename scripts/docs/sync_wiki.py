"""Validate GalNet docs and publish a managed, local GitHub Wiki working tree.

Python 3.11+, standard library only. This command never commits, fetches or pushes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import shutil
import sys
import unicodedata
import uuid
from urllib.parse import quote, unquote, urlsplit

START = '<!-- navigation:start -->'
END = '<!-- navigation:end -->'
MANIFEST = '.galnet-wiki.json'
LINK = re.compile(r'(!?\[[^\]\n]*\]\()(<[^>\n]+>|[^\s)]+)([^\n)]*\))')
REFERENCE = re.compile(r'^(\s{0,3}\[[^\]\n]+\]:\s*)(<[^>\n]+>|\S+)(.*)$')


class DocumentationError(ValueError):
    pass


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig')


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def managed_name(relative: str):
    if not (re.fullmatch(r'[A-Za-z0-9_-]+\.md', relative) or relative.startswith('_assets/')):
        raise DocumentationError(f'Invalid generated file name: {relative}')
    if any(part.startswith('.') for part in Path(relative).parts):
        raise DocumentationError(f'Hidden generated path: {relative}')


def contained(base: Path, relative: str) -> Path:
    if not relative or '\\' in relative or Path(relative).is_absolute() or '..' in Path(relative).parts:
        raise DocumentationError(f'Unsafe relative path: {relative}')
    candidate = base / relative
    if not candidate.resolve().is_relative_to(base.resolve()):
        raise DocumentationError(f'Path escapes root: {relative}')
    cursor = candidate
    while cursor != base:
        if cursor.is_symlink() or (hasattr(cursor, 'is_junction') and cursor.is_junction()):
            raise DocumentationError(f'Linked path is not managed: {relative}')
        cursor = cursor.parent
    return candidate


def prose_lines(text: str):
    """Yield (line, eligible) while preserving fenced blocks and inline code."""
    fence = None
    for line in text.splitlines(keepends=True):
        match = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if match:
            marker = match[1]
            if fence is None:
                fence = marker
            elif marker[0] == fence[0] and len(marker) >= len(fence) and not line.strip()[len(marker):]:
                fence = None
            yield line, False
        else:
            yield line, fence is None


def transform_links(text: str, transform) -> str:
    output = []
    for line, eligible in prose_lines(text):
        if eligible:
            reference = REFERENCE.match(line.rstrip('\r\n'))
            if reference:
                line = reference[1] + transform(reference[2].strip('<>')) + reference[3] + ('\n' if line.endswith('\n') else '')
            else:
                # A code span is opaque even when it contains Markdown-looking examples.
                parts = re.split(r'(`+[^`\n]*`+)', line)
                for index in range(0, len(parts), 2):
                    parts[index] = LINK.sub(lambda m: m[1] + transform(m[2].strip('<>')) + m[3], parts[index])
                line = ''.join(parts)
        output.append(line)
    return ''.join(output)


def heading_slug(title: str) -> str:
    title = re.sub(r'<[^>]+>', '', title)
    title = re.sub(r'\[([^]]+)\]\([^)]+\)', r'\1', title)
    title = title.replace('`', '').replace('*', '').lower().strip()
    return ''.join(c for c in title if c in '_-' or c.isspace() or unicodedata.category(c)[0] in 'LNM').replace(' ', '-')


def anchors(text: str) -> set[str]:
    result, counts = set(), {}
    for line, eligible in prose_lines(text):
        if not eligible:
            continue
        heading = re.match(r'^#{1,6}\s+(.+?)(?:\s+#+)?\s*$', line)
        if heading:
            slug = heading_slug(heading[1])
            number = counts.get(slug, 0)
            counts[slug] = number + 1
            result.add(slug + (f'-{number}' if number else ''))
        result.update(re.findall(r'<a\s+(?:name|id)=[\"\']([^\"\']+)', line))
    return result


def load_navigation(root: Path):
    docs = root / 'docs'
    config = json.loads(read(docs / 'navigation.json'))
    if config.get('schemaVersion') != 1:
        raise DocumentationError('Unsupported navigation schema')
    nodes, ids, paths = [], set(), set()

    def visit(node, parent=None, level=0):
        source, page_id, title = node['source'], node['id'], node['title']
        if not re.fullmatch(r'[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*', page_id):
            raise DocumentationError(f'Invalid Wiki ID: {page_id}')
        if page_id.casefold() in ids or source in paths:
            raise DocumentationError(f'Duplicate ID or source: {page_id}, {source}')
        ids.add(page_id.casefold()); paths.add(source)
        path = contained(docs, source)
        if not path.is_file() or path.suffix != '.md':
            raise DocumentationError(f'Missing Markdown page: {source}')
        first = next((line for line in read(path).splitlines() if line.strip()), '')
        if first != f'# {title}':
            raise DocumentationError(f'H1/title mismatch: {source}: {first!r} vs {title!r}')
        item = dict(node, parent=parent, level=level)
        nodes.append(item)
        for child in node.get('children', []):
            visit(child, item, level + 1)

    visit(config['root'])
    actual = {p.relative_to(docs).as_posix() for p in docs.rglob('*.md') if '_assets' not in p.relative_to(docs).parts}
    if paths != actual:
        raise DocumentationError(f'Navigation coverage: unlisted={sorted(actual-paths)}, missing={sorted(paths-actual)}')
    if config['root']['id'] != 'Home' or config['root']['source'] != 'README.md':
        raise DocumentationError('Root must map docs/README.md to Home')
    return config, nodes


def navigation_block(node) -> str:
    source_dir = Path(node['source']).parent
    lines = [START]
    def visit(children, depth):
        for child in children:
            target = os.path.relpath(child['source'], source_dir).replace('\\', '/')
            lines.append('  ' * depth + f'- [{child["title"]}]({target})')
            visit(child.get('children', []), depth + 1)
    visit(node.get('children', []), 0)
    lines.append(END)
    return '\n'.join(lines)


def update_indexes(root: Path, nodes, write=False):
    stale = []
    for node in nodes:
        if not node.get('children'):
            continue
        path = root / 'docs' / node['source']
        content = read(path)
        if content.count(START) != 1 or content.count(END) != 1 or content.index(START) > content.index(END):
            raise DocumentationError(f'Index must contain one navigation block: {node["source"]}')
        updated = re.sub(re.escape(START) + r'.*?' + re.escape(END), lambda _: navigation_block(node), content, flags=re.S)
        if updated != content:
            stale.append(node['source'])
            if write:
                path.write_text(updated, encoding='utf-8', newline='\n')
    if stale and not write:
        raise DocumentationError(f'Stale navigation blocks; run indexes: {stale}')
    return stale


def resolve_link(root: Path, source: Path, url: str):
    parsed = urlsplit(url)
    if parsed.scheme or parsed.netloc:
        return None
    if parsed.query:
        raise DocumentationError(f'Local link has unsupported query: {source}: {url}')
    path = (source.parent / unquote(parsed.path)).resolve() if parsed.path else source
    if not path.is_relative_to(root.resolve()):
        raise DocumentationError(f'Link escapes repository: {source}: {url}')
    if not path.exists():
        raise DocumentationError(f'Missing link: {source.relative_to(root)}: {url}')
    if path.is_dir() and (path / 'README.md').is_file():
        path /= 'README.md'
    if parsed.fragment and path.suffix.lower() == '.md' and unquote(parsed.fragment) not in anchors(read(path)):
        raise DocumentationError(f'Missing anchor: {source.relative_to(root)}: {url}')
    return path, parsed.fragment


def validate(root: Path, nodes):
    errors = []
    for node in nodes:
        path = root / 'docs' / node['source']
        def check(url):
            try:
                resolve_link(root, path, url)
            except DocumentationError as error:
                errors.append(str(error))
            return url
        transform_links(read(path), check)
    if errors:
        raise DocumentationError('\n'.join(errors))


def validate_repository_references(root: Path) -> int:
    """Check current docs targets from module entries and historical records too."""
    paths = [root / 'README.md'] if (root / 'README.md').exists() else []
    for folder in ['features','src','scripts','GameTestCase','skills','.agents']:
        for directory, children, filenames in os.walk(root / folder):
            children[:] = [name for name in children if name not in {'bin','obj','node_modules','.git','out','Temp','.galnet'}]
            paths.extend(Path(directory) / name for name in filenames if name.endswith('.md'))
    errors, count = [], 0
    for path in paths:
        def check(url):
            nonlocal count
            parsed = urlsplit(url)
            if not parsed.scheme and not parsed.netloc:
                target = (path.parent / unquote(parsed.path)).resolve() if parsed.path else path
                if target.is_relative_to(root / 'docs'):
                    count += 1
                    try:
                        resolve_link(root, path, url)
                    except DocumentationError as error:
                        errors.append(str(error))
            return url
        transform_links(read(path), check)
    if errors:
        raise DocumentationError('\n'.join(errors))
    return count


def render(root: Path, config, nodes, commit: str):
    if not re.fullmatch(r'[0-9a-f]{40,64}', commit):
        raise DocumentationError('Source commit must be a full Git hash')
    repository = config['repository'].rstrip('/')
    if not re.fullmatch(r'https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise DocumentationError('Invalid GitHub repository URL')
    docs = root / 'docs'
    page_by_source = {(docs / n['source']).resolve(): n for n in nodes}
    files = {}
    for node in nodes:
        path = docs / node['source']
        def convert(url):
            result = resolve_link(root, path, url)
            if result is None:
                return url
            target, fragment = result
            suffix = '#' + quote(unquote(fragment), safe='-_') if fragment else ''
            if target == path and not urlsplit(url).path:
                return suffix
            if target in page_by_source:
                return page_by_source[target]['id'] + suffix
            if target.is_relative_to(docs / '_assets'):
                relative = target.relative_to(docs).as_posix()
                files[relative] = target.read_bytes()
                return quote(relative, safe='/') + suffix
            relative = target.relative_to(root).as_posix()
            kind = 'tree' if target.is_dir() else 'blob'
            return f'{repository}/{kind}/{commit}/{quote(relative, safe="/")}' + suffix
        body = transform_links(read(path), convert)
        source_url = f'{repository}/blob/{commit}/docs/{quote(node["source"], safe="/")}'
        breadcrumb = f'[文档首页](Home)'
        if node['parent'] is not None:
            parent = node['parent']
            breadcrumb += f' · [上级：{parent["title"]}]({parent["id"]})'
        body += f'\n---\n\n{breadcrumb}\n\n由 [{node["source"]}]({source_url}) 生成。来源提交 `{commit[:12]}`；请在主仓库修改正文。\n'
        files[node['id'] + '.md'] = body.encode('utf-8')
    sidebar = '# 文档导航\n\n' + '\n'.join('  ' * n['level'] + f'- [{n["title"]}]({n["id"]})' for n in nodes) + '\n'
    files['_Sidebar.md'] = sidebar.encode('utf-8')
    files['_Footer.md'] = f'由主仓库 docs 生成 · 来源 `{commit[:12]}` · [文档首页](Home) · [维护说明](Manual-Development-Documentation)\n'.encode('utf-8')
    return files


def validate_rendered(files):
    """Check Wiki page IDs and fragments after conversion, including generated UI."""
    errors = []
    for name, data in files.items():
        if not name.endswith('.md'):
            continue
        def check(url):
            parsed = urlsplit(url)
            if parsed.scheme or parsed.netloc:
                return url
            relative = unquote(parsed.path)
            target = name if not relative else relative if relative.startswith('_assets/') else relative.removesuffix('.md') + '.md'
            if target not in files:
                errors.append(f'{name}: missing generated destination {url}')
            elif parsed.fragment and target.endswith('.md') and unquote(parsed.fragment) not in anchors(files[target].decode('utf-8')):
                errors.append(f'{name}: missing generated fragment {url}')
            return url
        transform_links(data.decode('utf-8'), check)
    if errors:
        raise DocumentationError('\n'.join(errors))


def verify_source_commit(root: Path, nodes, commit: str):
    """A published source link must identify the actual rendered document bytes."""
    for source in ['navigation.json'] + [node['source'] for node in nodes]:
        relative = 'docs/' + source
        result = subprocess.run(['git','-C',str(root),'show',f'{commit}:{relative}'], capture_output=True)
        if result.returncode or result.stdout.decode('utf-8-sig').replace('\r\n','\n') != read(root / relative):
            raise DocumentationError(f'Uncommitted/different source: {relative}; commit docs before generating Wiki provenance')


def synchronize(wiki: Path, files: dict[str, bytes], commit: str, require_git=True):
    wiki = wiki.resolve()
    if not wiki.is_dir() or (require_git and not (wiki / '.git').exists()):
        raise DocumentationError(f'Wiki checkout does not exist: {wiki}')
    if require_git:
        # A nested checkout must be the Wiki itself, never the parent source repo.
        top = subprocess.check_output(['git','-C',str(wiki),'rev-parse','--show-toplevel'], text=True).strip()
        if Path(top).resolve() != wiki:
            raise DocumentationError('Output must be an independent Wiki checkout')
        status = subprocess.run(['git','-C',str(wiki),'rev-list','--left-right','--count','HEAD...@{upstream}'], capture_output=True, text=True)
        if status.returncode == 0 and int(status.stdout.split()[1]):
            raise DocumentationError('Wiki is behind/diverged from its fetched upstream; reconcile before syncing')
    manifest_path = contained(wiki, MANIFEST)
    old = json.loads(read(manifest_path)) if manifest_path.exists() else {'schemaVersion':1,'files':{}}
    if old.get('schemaVersion') != 1 or not isinstance(old.get('files'), dict):
        raise DocumentationError('Invalid Wiki management manifest')
    previous = old['files']
    for relative, expected in previous.items():
        managed_name(relative)
        path = contained(wiki, relative)
        if not path.is_file() or digest(path.read_bytes()) != expected:
            raise DocumentationError(f'Manually changed/missing generated file: {relative}')
    for relative in files:
        managed_name(relative)
        path = contained(wiki, relative)
        if path.exists() and relative not in previous:
            initial_home = relative == 'Home.md' and read(path).strip() == 'Welcome to the Gal.Net wiki!'
            if not initial_home:
                raise DocumentationError(f'Refusing to replace unmanaged Wiki file: {relative}')
    # Build all bytes in staging before modifying the working tree. Only manifest-owned
    # files can be removed; caller must review/commit the resulting Git diff.
    # Normal inherited ACLs also work in restricted Windows execution hosts;
    # Python's Windows 0o700 tempfile ACL can deny the host's restricted token.
    stage = wiki / ('.galnet-wiki-' + uuid.uuid4().hex)
    stage.mkdir()
    try:
        for relative, data in files.items():
            dest = contained(stage, relative)
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
        for relative in sorted(set(previous) - set(files)):
            contained(wiki, relative).unlink()
        for relative in files:
            target = contained(wiki, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            data = (stage / relative).read_bytes()
            if not target.exists() or target.read_bytes() != data:
                target.write_bytes(data)
    finally:
        if stage.resolve().parent != wiki or stage.is_symlink():
            raise DocumentationError('Unexpected staging path during cleanup')
        shutil.rmtree(stage)
    manifest = {'schemaVersion':1,'sourceCommit':commit,'files':{p:digest(data) for p,data in sorted(files.items())}}
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['check','indexes','sync'])
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--wiki-dir', type=Path)
    parser.add_argument('--source-commit')
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        config, nodes = load_navigation(root)
        update_indexes(root, nodes, write=args.command == 'indexes')
        validate(root, nodes)
        references = validate_repository_references(root)
        if args.command == 'sync':
            commit = args.source_commit or subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'], text=True).strip()
            verify_source_commit(root, nodes, commit)
            wiki = args.wiki_dir or root / 'wiki'
            if wiki.resolve() == root or wiki.resolve().is_relative_to(root / 'docs'):
                raise DocumentationError('Wiki output overlaps source documentation')
            files = render(root, config, nodes, commit)
            validate_rendered(files)
            synchronize(wiki, files, commit)
            print(f'Generated {len(nodes)} complete pages into {wiki}; review and commit/push separately.')
        else:
            print(f'Validated {len(nodes)} pages, navigation, local links, heading fragments and {references} repository references.')
    except (DocumentationError, OSError, KeyError, json.JSONDecodeError, subprocess.CalledProcessError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
