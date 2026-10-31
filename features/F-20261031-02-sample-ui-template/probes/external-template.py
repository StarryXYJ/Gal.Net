"""Copy the UI template outside the checkout and test customizations against real packages."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repo = args.repository.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    root = Path(os.environ.get('TEMP', '/tmp')) / ('galnet-ui-template-' + uuid.uuid4().hex)
    root.mkdir()  # Inherit the parent's ACL; Python tempfile creates restrictive Windows ACLs.
    root = root.resolve()
    if root.is_relative_to(repo):
        raise RuntimeError('The consumer must be outside the checkout')
    app = root / 'GalNet.Sample.CrossPlatform'
    contracts = root / 'GalNet.Sample.Contracts'
    tests = root / 'TemplateProbe'
    source = repo / 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform'
    ignore = shutil.ignore_patterns('bin', 'obj', 'packages.lock*.json')
    shutil.copytree(source, app, ignore=ignore)
    shutil.copytree(repo / 'src/Samples/GalNet.Sample.Contracts', contracts, ignore=ignore)
    tests.mkdir()
    shutil.copy2(repo / 'global.json', root / 'global.json')
    central = ET.parse(repo / 'Directory.Packages.props')
    group = ET.SubElement(central.getroot(), 'ItemGroup')
    for name in ['GalNet.Avalonia', 'GalNet.Avalonia.Debugging', 'GalNet.Contracts', 'GalNet.SchemaGenerator']:
        ET.SubElement(group, 'PackageVersion', Include=name, Version=args.version)
    central.write(root / 'Directory.Packages.props', encoding='utf-8', xml_declaration=True)
    (root / 'Directory.Build.props').write_text('''<Project><PropertyGroup>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><LangVersion>latest</LangVersion>
<EnableGameDebugging>false</EnableGameDebugging><NuGetAudit>false</NuGetAudit>
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup></Project>''')
    configuration = ET.Element('configuration')
    feeds = ET.SubElement(configuration, 'packageSources')
    ET.SubElement(feeds, 'clear')
    asset_data = json.loads((repo / 'src/Shared/GalNet.Core/obj/project.assets.json').read_text(encoding='utf-8-sig'))
    sources = [str(args.packages.resolve()), *asset_data['packageFolders'], 'https://api.nuget.org/v3/index.json']
    for i, value in enumerate(sources):
        ET.SubElement(feeds, 'add', key='feed' + str(i), value=value)
    ET.ElementTree(configuration).write(root / 'NuGet.Config', encoding='utf-8', xml_declaration=True)
    project = app / 'GalNet.Sample.CrossPlatform.csproj'
    tree = ET.parse(project)
    package_ids = set()
    for item_group in tree.getroot().findall('ItemGroup'):
        for ref in list(item_group.findall('ProjectReference')):
            path = ref.get('Include', '')
            if 'GalNet.Sample.Contracts' in path:
                ref.set('Include', '../GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj')
                continue
            package_id = 'GalNet.Avalonia.Debugging' if 'Debugging' in path else 'GalNet.Avalonia'
            item_group.remove(ref)
            if package_id not in package_ids:
                package_ids.add(package_id)
                new_ref = ET.SubElement(item_group, 'PackageReference', Include=package_id)
                if ref.get('Condition'):
                    new_ref.set('Condition', ref.get('Condition'))
    tree.write(project, encoding='utf-8', xml_declaration=True)
    (contracts / 'GalNet.Sample.Contracts.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>GalNet.Project.Contracts</AssemblyName></PropertyGroup>
<ItemGroup><PackageReference Include="GalNet.Contracts"/><PackageReference Include="GalNet.SchemaGenerator" PrivateAssets="all"/></ItemGroup>
</Project>''')
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
<ItemGroup><ProjectReference Include="../GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.csproj"/>
<PackageReference Include="Avalonia.Headless"/><PackageReference Include="Microsoft.NET.Test.Sdk"/><PackageReference Include="NUnit"/><PackageReference Include="NUnit3TestAdapter"/>
<Using Include="NUnit.Framework"/></ItemGroup></Project>''')
    refs = [ref.get('Include') for p in root.rglob('*.csproj') for ref in ET.parse(p).getroot().iter('ProjectReference')]
    if any(repo == (root / ref).resolve() or str(repo).lower() in ref.lower() for ref in refs):
        raise RuntimeError('Repository project reference leaked into the consumer')
    record = {'consumer': str(root), 'packages': str(args.packages.resolve()), 'version': args.version,
              'sourceCommit': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
              'projectReferences': refs, 'customizedFiles': ['Views/TitlePage.axaml', 'ViewModels/TitlePageViewModel.cs',
              'Views/NotebookPage.axaml', 'Views/NotebookPage.axaml.cs', 'ViewModels/NotebookPageViewModel.cs',
              'SampleCustomization.cs', 'Views/GamePage.axaml', 'ViewModels/GamePageViewModel.cs']}
    (output / 'external-template.json').write_text(json.dumps(record, indent=2))
    for label, command in [('restore', ['dotnet', 'restore', str(tests / 'TemplateProbe.csproj'), '--configfile', str(root / 'NuGet.Config'), '--ignore-failed-sources']),
                           ('test', ['dotnet', 'test', str(tests / 'TemplateProbe.csproj'), '-c', 'Release', '--no-restore', '--logger', 'trx;LogFileName=external-template.trx', '--results-directory', str(output)])]:
        with (output / ('external-' + label + '.log')).open('w') as log:
            result = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT)
        print(label, result.returncode, flush=True)
        if result.returncode:
            raise RuntimeError('Consumer ' + label + ' failed; inspect ' + str(output))


if __name__ == '__main__':
    main()
