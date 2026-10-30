using Avalonia.Controls;
using Avalonia.Headless;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task NewProjectApplication_FindsDeclaredContractsCopiesOnlyDependenciesAndPersistsDebugApp()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var (executable, contracts) = CreateApplication();
            var dialogs = (TestDialogs)services.GetRequiredService<IFileDialogService>();
            dialogs.OpenPaths = [executable];
            var panel = services.GetRequiredService<NewProjectPanelViewModel>();
            var view = new NewProjectPanelView { DataContext = panel };
            var window = new Window { Width = 860, Height = 800, Content = view };
            window.Show();
            try
            {
                view.FindControl<TextBox>("ProjectNameInput")!.Text = "FromApp";
                view.FindControl<TextBox>("ProjectRootInput")!.Text = _root;
                await ((IAsyncRelayCommand)view.FindControl<Button>("BrowseApplicationButton")!.Command!).ExecuteAsync(null);
                Assert.That(view.FindControl<TextBox>("ApplicationPathInput")!.Text, Is.EqualTo(executable));
                Assert.That(view.FindControl<TextBox>("ContractsDirectoryInput")!.Text, Is.EqualTo(contracts));
                await ((IAsyncRelayCommand)view.FindControl<Button>("CreateProjectButton")!.Command!).ExecuteAsync(null);
                Assert.That(panel.Message, Is.Empty);
                var projects = services.GetRequiredService<IProjectService>();
                var path = projects.Current!.RootPath;
                var deployed = Directory.GetFiles(Path.Combine(path, "Contracts")).Select(Path.GetFileName).ToArray();
                Assert.That(deployed, Is.SupersetOf(new[] { "GalNet.Project.Contracts.dll", "GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "SchemaDependency.dll" }));
                Assert.That(deployed, Has.None.EqualTo("System.Private.CoreLib.dll"));
                Assert.That(deployed, Has.None.EqualTo("Unrelated.dll"));
                Assert.That(deployed, Has.None.EqualTo("native.dll"));
                Assert.That(deployed, Has.None.EqualTo("Game.pdb"));
                Assert.That(deployed, Has.None.EqualTo("Game.exe"));
                Assert.That(File.ReadAllBytes(Path.Combine(path, "Contracts", ProjectSchemaContext.AssemblyFileName)),
                    Is.EqualTo(File.ReadAllBytes(Path.Combine(contracts, ProjectSchemaContext.AssemblyFileName))));
                await projects.CloseAsync();
                var reopened = await projects.OpenAsync(path);
                var launch = ExternalGameDebugClient.LoadSettings(reopened.RootPath)!;
                Assert.That(launch.Executable, Is.EqualTo(executable));
                Assert.That(launch.Arguments, Is.Empty);
                Assert.That(launch.WorkingDirectory, Is.Null);
                Assert.That(reopened.Services.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>().Get("custom.example"), Is.Not.Null);
                await projects.CloseAsync();
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task NewProjectApplication_PreservesExplicitContractAndPickerCancellation()
    {
        using var services = Services();
        var dialogs = (TestDialogs)services.GetRequiredService<IFileDialogService>();
        var panel = services.GetRequiredService<NewProjectPanelViewModel>();
        panel.ContractsDirectory = "  explicit contract  ";
        var (executable, _) = CreateApplication();
        dialogs.OpenPaths = [executable];
        await panel.BrowseApplicationCommand.ExecuteAsync(null);
        Assert.That(panel.ContractsDirectory, Is.EqualTo("  explicit contract  "));
        dialogs.OpenPaths = [];
        await panel.BrowseApplicationCommand.ExecuteAsync(null);
        Assert.That(panel.ApplicationPath, Is.EqualTo(executable));
        Assert.That(panel.ContractsDirectory, Is.EqualTo("  explicit contract  "));
    }

    [TestCase("missing")]
    [TestCase("invalid")]
    [TestCase("dependency")]
    public async Task NewProjectApplication_InvalidSourceDoesNotCreateOrFallBackToDefaults(string failure)
    {
        using var services = Services();
        var (executable, contracts) = CreateApplication();
        if (failure == "missing") File.Delete(Path.Combine(contracts, ProjectSchemaContext.AssemblyFileName));
        if (failure == "invalid") File.WriteAllText(Path.Combine(contracts, ProjectSchemaContext.AssemblyFileName), "invalid DLL");
        if (failure == "dependency") File.Delete(Path.Combine(contracts, "SchemaDependency.dll"));
        var panel = services.GetRequiredService<NewProjectPanelViewModel>();
        panel.ProjectName = "Rejected";
        panel.ProjectRoot = _root;
        panel.ApplicationPath = executable;
        if (failure is "missing")
        {
            Assert.That(panel.ContractsDirectory, Is.Empty);
            Assert.That(panel.Message, Is.Not.Empty);
        }
        await panel.CreateCommand.ExecuteAsync(null);
        Assert.That(panel.Message, Is.Not.Empty);
        Assert.That(services.GetRequiredService<IProjectService>().Current, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_root, "Rejected")), Is.False);
    }

    [Test]
    public async Task NewProjectApplication_ManualContractsCanResolveMissingAutomaticSource()
    {
        using var services = Services();
        var (executable, contracts) = CreateApplication();
        File.Delete(Path.Combine(contracts, ProjectSchemaContext.AssemblyFileName));
        var panel = services.GetRequiredService<NewProjectPanelViewModel>();
        panel.ProjectName = "Manual";
        panel.ProjectRoot = _root;
        panel.ApplicationPath = executable;
        Assert.That(panel.Message, Is.Not.Empty);
        var dialogs = (TestDialogs)services.GetRequiredService<IFileDialogService>();
        dialogs.Folder = Contracts();
        await panel.BrowseContractsCommand.ExecuteAsync(null);
        Assert.That(panel.Message, Is.Empty);
        await panel.CreateCommand.ExecuteAsync(null);
        Assert.That(panel.Message, Is.Empty);
        var projects = services.GetRequiredService<IProjectService>();
        Assert.That(ExternalGameDebugClient.LoadSettings(projects.Current!.RootPath)!.Executable, Is.EqualTo(executable));
        await projects.CloseAsync();
    }

    [Test]
    public void NewProjectApplication_LinuxPackageLauncherFindsDeclaredContracts()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "Linux Game App")).FullName;
        var contracts = Directory.CreateDirectory(Path.Combine(root, "lib")).FullName;
        foreach (var file in Directory.GetFiles(Contracts(), "*.dll"))
            File.Copy(file, Path.Combine(contracts, Path.GetFileName(file)));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "application-layout.json"),
            """{"schemaVersion":1,"runtimeIdentifier":"linux-x64","entryPoint":"GalNet.Sample.CrossPlatform","managedDirectory":"lib","contractsDirectory":"lib","contentDirectory":"pak"}""");
        var launcher = Path.Combine(root, "GalNet.Sample.CrossPlatform");
        File.WriteAllBytes(launcher, []);

        Assert.That(ProjectContractSource.RequireApplication(launcher), Is.EqualTo(launcher));
        Assert.That(ProjectContractSource.FindForApplication(launcher), Is.EqualTo(contracts));

        var undeclared = Path.Combine(root, "not-the-launcher");
        File.WriteAllBytes(undeclared, []);
        Assert.That(() => ProjectContractSource.RequireApplication(undeclared),
            Throws.TypeOf<EditorOperationException>());
    }

    private (string Executable, string Contracts) CreateApplication()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "Game App")).FullName;
        var contracts = Directory.CreateDirectory(Path.Combine(root, "lib")).FullName;
        foreach (var file in Directory.GetFiles(Contracts(), "*.dll"))
            File.Copy(file, Path.Combine(contracts, Path.GetFileName(file)));
        // A self-contained App also carries framework DLLs and unrelated managed/native payloads.
        File.Copy(typeof(object).Assembly.Location, Path.Combine(contracts, "System.Private.CoreLib.dll"));
        File.Copy(typeof(NewProjectPanelViewModel).Assembly.Location, Path.Combine(contracts, "Unrelated.dll"));
        File.WriteAllText(Path.Combine(contracts, "native.dll"), "native payload");
        File.WriteAllText(Path.Combine(contracts, "Game.pdb"), "symbols");
        var executable = Path.Combine(root, "Game.exe");
        File.WriteAllBytes(executable, []);
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "application-layout.json"),
            """{"schemaVersion":1,"runtimeIdentifier":"win-x64","entryPoint":"Game.exe","managedDirectory":"lib","contractsDirectory":"lib","contentDirectory":"pak"}""");
        return (executable, contracts);
    }
}
