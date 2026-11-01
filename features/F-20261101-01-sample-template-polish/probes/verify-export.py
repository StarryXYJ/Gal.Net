"""Customize a manifest-copied Sample and test its real package closure without rewriting project XML."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--consumer', type=Path, required=True)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repo, root, output = args.repository.resolve(), args.consumer.resolve(), args.output.resolve()
    if root.is_relative_to(repo):
        raise ValueError('Use a repository-external consumer')
    output.mkdir(parents=True, exist_ok=True)
    app = root / 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform'
    tests = root / 'TemplateProbe'
    tests.mkdir()
    manifest = json.loads((root / 'template-manifest.json').read_text(encoding='utf-8'))
    project_hashes = {item['path']: item['sha256'] for item in manifest['files'] if item['path'].endswith('.csproj')}
    properties = ['-p:GalNetPackageVersion=' + args.version, '-p:EnableGameDebugging=false',
                  '-p:Configuration=Release', '-p:NuGetAudit=false']
    asset_data = json.loads((repo / 'src/Shared/GalNet.Core/obj/project.assets.json').read_text(encoding='utf-8-sig'))
    configuration = ET.Element('configuration')
    sources = ET.SubElement(configuration, 'packageSources')
    ET.SubElement(sources, 'clear')
    for index, path in enumerate([str(args.packages.resolve()), *asset_data['packageFolders'], 'https://api.nuget.org/v3/index.json']):
        ET.SubElement(sources, 'add', key='feed' + str(index), value=path)
    ET.ElementTree(configuration).write(root / 'NuGet.Config', encoding='utf-8', xml_declaration=True)
    # The following are project-source edits only: no engine source or default VM inheritance.
    title_xaml = app / 'Views/TitlePage.axaml'
    text = title_xaml.read_text(encoding='utf-8-sig')
    text = text.replace('Content="{text:PlayerText Title.NewGame, \'New Game\'}"', 'Content="Start my story" Background="Magenta"')
    pattern = r'(<Button x:Name="NewGameButton"[^>]*?)/>'
    replacement = r'''\1><Button.Theme><ControlTheme TargetType="Button">
      <Setter Property="Template"><ControlTemplate><Border x:Name="ProjectButtonSurface" Background="{TemplateBinding Background}" Padding="18" CornerRadius="18">
      <ContentPresenter Content="{TemplateBinding Content}" HorizontalAlignment="Center"/></Border></ControlTemplate></Setter>
    </ControlTheme></Button.Theme></Button>'''
    text, count = re.subn(pattern, replacement, text)
    if count != 1:
        raise RuntimeError('Expected one project start button')
    title_xaml.write_text(text)
    title_model = app / 'ViewModels/TitlePageViewModel.cs'
    text = title_model.read_text(encoding='utf-8-sig')
    extra = '''
    public int CustomizationCount { get; private set; }
    [RelayCommand] private void IncrementCustomization() { CustomizationCount++; OnPropertyChanged(nameof(CustomizationCount)); }
    [RelayCommand] private void OpenNotebook() => _navigation.Navigate<NotebookPageViewModel>();
'''
    text = text[:text.rindex('}')] + extra + '}\n'
    title_model.write_text(text)
    (app / 'ViewModels/NotebookPageViewModel.cs').write_text('''using GalNet.Avalonia.GameView.Navigation;
namespace GalNet.Sample.CrossPlatform.ViewModels;
public sealed class NotebookPageViewModel(IGameNavigationService navigation) : BackNavigablePageViewModel(navigation);
''')
    (app / 'Views/NotebookPage.axaml').write_text('''<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:vm="using:GalNet.Sample.CrossPlatform.ViewModels"
x:Class="GalNet.Sample.CrossPlatform.Views.NotebookPage" x:DataType="vm:NotebookPageViewModel"><StackPanel>
<TextBlock Text="Project notebook"/><Button Content="Back" Command="{Binding BackCommand}"/></StackPanel></UserControl>''')
    (app / 'Views/NotebookPage.axaml.cs').write_text('''namespace GalNet.Sample.CrossPlatform.Views;
public partial class NotebookPage : global::Avalonia.Controls.UserControl { public NotebookPage() => InitializeComponent(); }
''')
    customization = app / 'SampleCustomization.cs'
    text = customization.read_text(encoding='utf-8-sig').replace('= GamePalette.Mono;', '= GamePalette.Rose;')
    text = text.replace('                configureViews?.Invoke(views);', '                views.Register<ViewModels.NotebookPageViewModel, Views.NotebookPage>();\n                configureViews?.Invoke(views);')
    customization.write_text(text)
    gameplay = app / 'ViewModels/GamePageViewModel.cs'
    text = gameplay.read_text(encoding='utf-8-sig')
    text = text[:text.rindex('}')] + '    [RelayCommand] private void OpenNotebook() => navigation.Navigate<NotebookPageViewModel>();\n}\n'
    gameplay.write_text(text)
    gameplay_xaml = app / 'Views/GamePage.axaml'
    text = gameplay_xaml.read_text(encoding='utf-8-sig').replace('<Button x:Name="SettingsButton"', '<Button x:Name="NotebookButton" Content="Notebook" Command="{Binding OpenNotebookCommand}"/>\n                <Button x:Name="SettingsButton"')
    gameplay_xaml.write_text(text)
    # Run the same template behavior fixture against the copied source and packaged mechanisms.
    fixture = (repo / 'test/GalNet.IntegrationTests/SampleTemplatePagesTests.cs').read_text(encoding='utf-8-sig')
    marker = '    private static ServiceCollection Services('
    probe = '''    [Test]
    public Task FiveProjectCustomizationsWorkWithoutEngineSource() => SampleUiTests.RunAsync(async () =>
    {
        await using var provider = Services(new Session()).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<SampleTitle>(NavigationTransition.None);
        var window = new Window { Width = 1280, Height = 720, Content = services.GetRequiredService<GameShell>() };
        window.Show();
        try
        {
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "NewGameButton");
            Assert.That(button.Content, Is.EqualTo("Start my story"));
            Assert.That(button.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "ProjectButtonSurface"), Is.True);
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)button.Background!).Color, Is.EqualTo(global::Avalonia.Media.Colors.Magenta));
            Assert.That(SampleCustomization.PlayerPalette, Is.SameAs(GalNet.Avalonia.Themes.GamePalette.Rose));
            var title = services.GetRequiredService<SampleTitle>();
            title.IncrementCustomizationCommand.Execute(null);
            Assert.That(title.CustomizationCount, Is.EqualTo(1));
            title.OpenNotebookCommand.Execute(null);
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<NotebookPageViewModel>());
            ((NotebookPageViewModel)navigation.CurrentViewModel!).BackCommand.Execute(null);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(title));
            await navigation.ResetToAsync<SampleGame>(NavigationTransition.None);
            var game = services.GetRequiredService<SampleGame>(); game.State.IsGameUiReady = true;
            var advances = 0; game.State.AdvanceRequested += () => advances++;
            window.UpdateLayout();
            button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "NotebookButton");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<NotebookPageViewModel>());
            Assert.That(advances, Is.Zero);
            Assert.That(typeof(GamePresentationState).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion, Does.StartWith("VERSION"));
        }
        finally { window.Close(); }
    });

'''.replace('VERSION', args.version)
    fixture = fixture.replace(marker, probe + marker)
    (tests / 'SampleTemplatePagesTests.cs').write_text(fixture)
    (tests / 'Ui.cs').write_text('''using Avalonia;
using Avalonia.Headless;
using NUnit.Framework;
[assembly: AvaloniaTestApplication(typeof(ProbeApplication))]
public static class ProbeApplication { public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseSkia().With(GalNet.Avalonia.Themes.GameTypography.CreateFontManagerOptions()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }); }
namespace GalNet.IntegrationTests { internal static class SampleUiTests { public static Task RunAsync(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ProbeApplication).Assembly).Dispatch<bool>(async () => { await action(); return true; }, TestContext.CurrentContext.CancellationToken); } }
''')
    (tests / 'TemplateProbe.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup>
<ItemGroup><ProjectReference Include="../src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.csproj"/>
<PackageReference Include="Avalonia.Headless"/><PackageReference Include="Microsoft.NET.Test.Sdk"/><PackageReference Include="NUnit"/><PackageReference Include="NUnit3TestAdapter"/>
<Using Include="NUnit.Framework"/></ItemGroup></Project>''')
    refs = [ref.get('Include') for p in root.rglob('*.csproj') for ref in ET.parse(p).getroot().iter('ProjectReference')]
    if any(repo == (root / ref).resolve() or str(repo).lower() in ref.lower() for ref in refs):
        raise RuntimeError('Repository project reference leaked into the consumer')
    record = {'consumer': str(root), 'packages': str(args.packages.resolve()), 'version': args.version,
              'sourceCommit': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
              'declaredProjectReferences': refs, 'customizedFiles': ['Views/TitlePage.axaml', 'ViewModels/TitlePageViewModel.cs',
              'Views/NotebookPage.axaml', 'Views/NotebookPage.axaml.cs', 'ViewModels/NotebookPageViewModel.cs',
              'SampleCustomization.cs', 'Views/GamePage.axaml', 'ViewModels/GamePageViewModel.cs']}
    (output / 'external-template.json').write_text(json.dumps(record, indent=2))
    for label, command in [('restore', ['dotnet', 'restore', str(tests / 'TemplateProbe.csproj'), *properties, '--configfile', str(root / 'NuGet.Config'), '--ignore-failed-sources']),
                           ('test', ['dotnet', 'test', str(tests / 'TemplateProbe.csproj'), *properties, '-c', 'Release', '--no-restore', '--logger', 'trx;LogFileName=external-template.trx', '--results-directory', str(output)])]:
        with (output / ('external-' + label + '.log')).open('w') as log:
            result = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT)
        print(label, result.returncode, flush=True)
        if result.returncode:
            raise RuntimeError('Consumer ' + label + ' failed; inspect ' + str(output))


    for relative, digest in project_hashes.items():
        if hashlib.sha256((root / relative).read_bytes()).hexdigest() != digest:
            raise RuntimeError('A copied project was rewritten: ' + relative)
    assets = json.loads((tests / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
    project_names = [name for name, item in assets['libraries'].items() if item['type'] == 'project']
    allowed = {'GalNet.Sample.CrossPlatform', 'GalNet.Project.Contracts'}
    if any(name.split('/')[0] not in allowed for name in project_names):
        raise RuntimeError('Engine source entered the consumer graph: ' + str(project_names))
    if any(name.split('/')[0] == 'LibVLCSharp.Avalonia' for name in assets['libraries']):
        raise RuntimeError('Unused video control package entered the consumer graph')
    record.update(copiedProjectXmlUnchanged=True, evaluatedProjectLibraries=project_names,
                  sourceManifest=json.loads((root / 'template-manifest.json').read_text(encoding='utf-8')))
    (output / 'external-template.json').write_text(json.dumps(record, indent=2))


if __name__ == '__main__':
    main()
