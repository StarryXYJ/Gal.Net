"""Build a dotnet-new template package from the maintained cross-platform Sample."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import zipfile
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]
SAMPLE = Path('src/Samples/GalNet.Sample.CrossPlatform')


def change(path, old, new):
    text = path.read_text(encoding='utf-8-sig')
    if old not in text:
        raise ValueError(f'Expected template input not found in {path}: {old}')
    path.write_text(text.replace(old, new), encoding='utf-8', newline='')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True, help='Template and GalNet package version')
    parser.add_argument('--work-directory', type=Path, required=True, help='New staging directory')
    parser.add_argument('--output-directory', type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?', args.version):
        parser.error('--version must be a NuGet version such as 1.2.3 or 1.2.3-preview.1')
    work = args.work_directory.resolve()
    subprocess.run(['python', str(ROOT / 'scripts/samples/export-template.py'), '--output', str(work)], check=True)
    (work / 'template-manifest.json').unlink()
    app = work / SAMPLE / 'GalNet.Sample.CrossPlatform'
    change(app / 'GalNet.Sample.CrossPlatform.csproj', '<TargetFramework>net10.0</TargetFramework>',
           '<TargetFramework>net10.0</TargetFramework>\n    <AssemblyName>GalNet.Sample.CrossPlatform</AssemblyName>')
    for path in (app / 'Localization').glob('*.json'):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        original = json.dumps(data['App.Title'], ensure_ascii=False)
        change(path, original, '"GALNET_JSON_TITLE"')
    change(app / 'SampleCustomization.cs', 'GameTitle = "GalNet CrossPlatform Sample"',
           'GameTitle = "GALNET_JSON_TITLE"')
    android = work / SAMPLE / 'GalNet.Sample.CrossPlatform.Android'
    change(android / 'MainActivity.cs', 'Label = "GalNet.Sample.CrossPlatform.Android"',
           'Label = "GALNET_JSON_TITLE"')
    change(android / 'Properties/AndroidManifest.xml', 'android:label="GalNet.Sample.CrossPlatform"',
           'android:label="GALNET_XML_TITLE"')
    change(android / 'GalNet.Sample.CrossPlatform.Android.csproj', 'org.galnet.sample', 'org.example.gameGALNET_APP_ID')
    ios = work / SAMPLE / 'GalNet.Sample.CrossPlatform.iOS'
    change(ios / 'Info.plist', '<string>GalNet.Sample.CrossPlatform</string>', '<string>GALNET_XML_TITLE</string>')
    change(ios / 'Info.plist', 'companyName.GalNet.Sample.CrossPlatform', 'org.example.gameGALNET_APP_ID')
    change(ios / 'Resources/LaunchScreen.xib', 'text="GalNet.Sample.CrossPlatform"', 'text="GALNET_XML_TITLE"')
    change(work / SAMPLE / 'GalNet.Sample.CrossPlatform.Browser/wwwroot/index.html',
           '<title>GalNet.Sample.CrossPlatform.Browser</title>', '<title>GALNET_XML_TITLE</title>')
    solution = work / SAMPLE / 'GalNet.Sample.CrossPlatform.slnx'
    solution_text = solution.read_text(encoding='utf-8-sig')
    solution_text = solution_text.replace('<Project Path="', '<Project Path="src/Samples/GalNet.Sample.CrossPlatform/')
    solution_text = solution_text.replace('</Solution>',
        '  <Project Path="src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj" />\n</Solution>')
    (work / 'GalNet.Sample.CrossPlatform.slnx').write_text(solution_text, encoding='utf-8')
    solution.unlink()
    (work / 'README.md').write_text((ROOT / 'src/Samples/GalNet.Sample.CrossPlatform/Template/DotnetNew.README.md').read_text(encoding='utf-8'), encoding='utf-8')
    props = work / 'Directory.Build.props'
    change(props, '<GalNetUsePackages>true</GalNetUsePackages>',
           f'<GalNetUsePackages>true</GalNetUsePackages>\n    <GalNetPackageVersion Condition="\'$(GalNetPackageVersion)\' == \'\'">{escape(args.version)}</GalNetPackageVersion>')
    forms = {
        'slash': {'identifier': 'replace', 'pattern': r'\\', 'replacement': r'\\'},
        'quote': {'identifier': 'replace', 'pattern': '"', 'replacement': r'\"'},
        'json': {'identifier': 'chain', 'steps': ['slash', 'quote']},
        'xml': {'identifier': 'xmlEncode'},
        'xmlQuote': {'identifier': 'replace', 'pattern': '"', 'replacement': '&quot;'},
        'xmlAttribute': {'identifier': 'chain', 'steps': ['xml', 'xmlQuote']},
        'id': {'identifier': 'replace', 'pattern': '[^a-zA-Z0-9_]', 'replacement': ''},
        'lower': {'identifier': 'lowerCaseInvariant'},
        'appId': {'identifier': 'chain', 'steps': ['id', 'lower']},
    }
    config = {
        '$schema': 'http://json.schemastore.org/template', 'author': 'GalNet',
        'identity': 'GalNet.CrossPlatform', 'name': 'GalNet cross-platform game',
        'shortName': 'galnet-game', 'sourceName': 'GalNet.Sample.CrossPlatform',
        'symbols': {
            'title': {'type': 'parameter', 'datatype': 'string', 'description': 'Game title'},
            'gameTitle': {'type': 'generated', 'generator': 'coalesce',
                          'parameters': {'sourceVariableName': 'title', 'fallbackVariableName': 'name'}},
            'jsonTitle': {'type': 'derived', 'valueSource': 'gameTitle', 'valueTransform': 'json', 'replaces': 'GALNET_JSON_TITLE'},
            'xmlTitle': {'type': 'derived', 'valueSource': 'gameTitle', 'valueTransform': 'xmlAttribute', 'replaces': 'GALNET_XML_TITLE'},
            'appId': {'type': 'derived', 'valueSource': 'name', 'valueTransform': 'appId', 'replaces': 'GALNET_APP_ID'},
        }, 'forms': forms,
    }
    config_dir = work / '.template.config'
    config_dir.mkdir()
    (config_dir / 'template.json').write_text(json.dumps(config, indent=2) + '\n', encoding='utf-8')
    args.output_directory.mkdir(parents=True, exist_ok=True)
    package = args.output_directory / f'GalNet.Templates.{args.version}.nupkg'
    if package.exists():
        raise FileExistsError(package)
    nuspec = f'''<?xml version="1.0" encoding="utf-8"?>
<package><metadata><id>GalNet.Templates</id><version>{escape(args.version)}</version><authors>GalNet</authors>
<description>Cross-platform GalNet game template.</description><packageTypes><packageType name="Template" /></packageTypes></metadata></package>'''
    with zipfile.ZipFile(package, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('GalNet.Templates.nuspec', nuspec)
        extensions = {p.suffix[1:].lower() for p in work.rglob('*') if p.is_file() and p.suffix}
        extensions.update({'rels', 'nuspec'})
        content_types = ''.join(
            f'<Default Extension="{escape(ext)}" ContentType="application/octet-stream" />'
            for ext in sorted(extensions) if ext != 'rels')
        archive.writestr('[Content_Types].xml', '<?xml version="1.0" encoding="utf-8"?>'
                         '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                         '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />'
                         + content_types + '</Types>')
        archive.writestr('_rels/.rels', '<?xml version="1.0" encoding="utf-8"?>'
                         '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                         '<Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" '
                         'Target="/GalNet.Templates.nuspec" Id="manifest" /></Relationships>')
        for path in sorted(work.rglob('*')):
            if path.is_file():
                archive.write(path, 'content/' + path.relative_to(work).as_posix())
    print(package)


if __name__ == '__main__':
    main()
