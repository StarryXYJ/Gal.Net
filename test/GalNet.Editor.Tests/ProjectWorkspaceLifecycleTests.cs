using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.Dock;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

[NonParallelizable]
public sealed class ProjectWorkspaceLifecycleTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-authoring-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [TestCase(false)]
    [TestCase(true)]
    public async Task NewProjectView_CreatesWithDefaultOrCustomContractWithoutStartingPreview(bool customContract)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Application.Current.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services();
            var dialogs = (TestDialogs)services.GetRequiredService<IFileDialogService>();
            dialogs.Folder = Contracts();
            var viewModel = services.GetRequiredService<NewProjectPanelViewModel>();
            var view = new NewProjectPanelView { DataContext = viewModel };
            var window = new Window { Width = 860, Height = 680, Content = view };
            window.Show();
            window.UpdateLayout();
            try
            {
                view.FindControl<TextBox>("ProjectNameInput")!.Text = "Authored";
                view.FindControl<TextBox>("ProjectRootInput")!.Text = _root;
                if (customContract)
                {
                    var browse = (IAsyncRelayCommand)view.FindControl<Button>("BrowseContractsButton")!.Command!;
                    await browse.ExecuteAsync(null);
                    Assert.That(view.FindControl<TextBox>("ContractsDirectoryInput")!.Text, Is.EqualTo(dialogs.Folder));
                }
                else Assert.That(viewModel.ContractsDirectory, Is.Empty);
                window.UpdateLayout();
                Assert.That(view.FindControl<TextBox>("ContractsDirectoryInput")!.GetVisualDescendants()
                    .OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(), Is.True, "The actual text editor template must be mounted.");
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                {
                    Directory.CreateDirectory(artifacts);
                    frame!.Save(Path.Combine(artifacts, customContract ? "p1a-new-project.png" : "p1a-new-project-default.png"));
                }
                await ((IAsyncRelayCommand)view.FindControl<Button>("CreateProjectButton")!.Command!).ExecuteAsync(null);
                Assert.That(viewModel.Message, Is.Empty);
                var projects = services.GetRequiredService<IProjectService>();
                Assert.That(projects.Current!.Name, Is.EqualTo("Authored"));
                Assert.That(services.GetRequiredService<INavigationService>().CurrentPage, Is.TypeOf<EditorPageViewModel>());
                Assert.That(projects.Current.Services.GetRequiredService<EditorWorkspaceViewModel>().ActivePreview, Is.Null);
                Assert.That(services.GetRequiredService<IEditorExtensionRegistry>().FindDockPanel(EditorDockPanelIds.GamePreview), Is.Null);
                var projectPath = projects.Current.RootPath;
                Assert.That(File.Exists(Path.Combine(projectPath, "Contracts", ProjectSchemaContext.AssemblyFileName)), Is.True);
                await projects.CloseAsync();
                var reopened = await projects.OpenAsync(projectPath);
                var catalog = reopened.Services.GetRequiredService<IEntryCatalog>();
                Assert.That(catalog.Get("dialogue.text"), Is.Not.Null);
                await projects.CloseAsync();
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupPagesShareProjectContentAndClosingThemDoesNotDiscardEdits()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "GroupPages"), "Pages", new(), Contracts());
            var scope = project.Services;
            var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
            var contribution = services.GetRequiredService<IEditorExtensionRegistry>().FindDockPanel(EditorDockPanelIds.GroupEditor)!;
            var first = (GroupEditorPanelViewModel)contribution.CreateViewModel(scope, group.Id);
            var second = (GroupEditorPanelViewModel)contribution.CreateViewModel(scope, group.Id);
            var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
            entry.Type = "dialogue.text";
            entry.Parameters = new() { ["content"] = "edited before closing" };
            var documents = scope.GetRequiredService<IEditorDocumentService>();
            Assert.That(documents.Groups[group.Id].Entries.Single(), Is.SameAs(entry.Source));
            Assert.That(second.GroupNode.Entries.Single(), Is.SameAs(first.GroupNode.Entries.Single()));
            Assert.That(entry.Source.Parameters["content"].GetString(), Is.EqualTo("edited before closing"));
            first.Dispose();
            second.Dispose();
            var third = (GroupEditorPanelViewModel)contribution.CreateViewModel(scope, group.Id);
            Assert.That(third.GroupNode.Entries.Single(), Is.SameAs(entry));
            await workspace.SaveAsync();
            third.Dispose();
            var sourceFile = documents.Groups[group.Id].RelativePath;
            await projects.CloseAsync();
            var reopened = await projects.OpenAsync(project.RootPath);
            var reloaded = reopened.Services.GetRequiredService<IEditorDocumentService>();
            Assert.That(reloaded, Is.Not.SameAs(documents));
            Assert.That(reloaded.Groups[group.Id].RelativePath, Is.EqualTo(sourceFile));
            Assert.That(reloaded.Groups[group.Id].Entries.Single().Parameters["content"].GetString(), Is.EqualTo("edited before closing"));
            Assert.That(() => services.GetRequiredService<IEditorDocumentService>(), Throws.InvalidOperationException);
            await projects.CloseAsync();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task StartupDisplaysFailedOpenAndLeavesTheInvalidGraphUntouched()
    {
        using var services = Services();
        var invalid = Directory.CreateDirectory(Path.Combine(_root, "Invalid")).FullName;
        var dialogs = (TestDialogs)services.GetRequiredService<IFileDialogService>();
        dialogs.Folder = invalid;
        var startup = services.GetRequiredService<StartupPageViewModel>();
        await startup.OpenProjectCommand.ExecuteAsync(null);
        Assert.That(startup.ErrorText, Does.Contain("contract").IgnoreCase);
        Assert.That(services.GetRequiredService<IProjectService>().Current, Is.Null);
        Assert.That(Directory.GetFiles(invalid, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task CancelNewProjectDoesNotCreateFiles()
    {
        using var services = Services();
        var viewModel = services.GetRequiredService<NewProjectPanelViewModel>();
        viewModel.ProjectRoot = _root;
        viewModel.ProjectName = "Cancelled";
        viewModel.CancelCommand.Execute(null);
        Assert.That(services.GetRequiredService<INavigationService>().CurrentPage, Is.TypeOf<StartupPageViewModel>());
        Assert.That(Directory.GetFileSystemEntries(_root), Is.Empty);
        await services.GetRequiredService<IProjectService>().CloseAsync();
    }

    [Test]
    public async Task SettingsSaveDerivesVariablesFromTheGraphAndPreservesOtherConfiguration()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Settings"), "Settings", new() { Speakers = ["narrator"] }, Contracts());
        var value = new Variable { Name = "score" };
        value.SetValue(4);
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        documents.CurrentDocument.PlayerVariables.Add(new() { Name = "score", DefaultValue = value });
        project.Settings.PlayerVariables.Clear();
        await projects.SaveAsync();
        using var saved = JsonDocument.Parse(File.ReadAllText(project.SettingsFilePath));
        Assert.That(saved.RootElement.GetProperty("playerVariables")[0].GetProperty("name").GetString(), Is.EqualTo("score"));
        Assert.That(project.Settings.Speakers.Single(), Is.EqualTo("narrator"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task InvalidEntryRootFailsBeforePublishingAProjectAndDoesNotGenerateSampleContent()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "InvalidRoot"), "Invalid", new(), Contracts());
        await projects.CloseAsync();
        var graphFile = Path.Combine(project.GraphPath, "graph.json");
        const string invalidGraph = """{"version":2,"name":"Invalid","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""";
        File.WriteAllText(graphFile, invalidGraph);
        try
        {
            await projects.OpenAsync(project.RootPath);
            Assert.Fail("Opening a graph without an explicit Entry must fail.");
        }
        catch (InvalidDataException exception) { Assert.That(exception.Message, Does.Contain("Entry")); }
        Assert.That(projects.Current, Is.Null);
        Assert.That(File.ReadAllText(graphFile), Is.EqualTo(invalidGraph));
    }

    [Test]
    public async Task ProjectDisposalInvalidatesBindingsEvenForEntriesRetainedOnlyByHistory()
    {
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom",
            [new DefaultPrimitiveEntryBase("custom.value", new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int))]))])]);
        var documents = new EditorDocumentService();
        documents.Load(new()
        {
            Document = new() { Nodes = [new() { Id = "group", Type = "Group", File = "groups/group.rawgalgroup" }] },
            GroupEntries = { ["group"] = [new() { StableId = "line", Type = "custom.value", Parameters = new() { ["count"] = JsonSerializer.SerializeToElement(4) } }] }
        });
        var store = new EditorGroupProjectionStore(documents, catalog);
        var group = store.Get("group");
        var entry = group.Entries.Single();
        entry.ConfigureParameterFields(catalog, [], []);
        var field = (IFieldBinding)entry.ParameterFields.Single();
        group.Entries.Remove(entry);
        store.Dispose();
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null);
        Assert.That(entry.Source.Parameters["count"].GetInt32(), Is.EqualTo(4));
    }

    private static ServiceProvider Services() => new ServiceCollection().AddEditorServices()
        .AddSingleton<IEditorSettingsService>(new TestSettings())
        .AddSingleton<IFileDialogService>(new TestDialogs())
        .AddSingleton<IEffectCatalog>(new EffectCatalog([]))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static string Contracts()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(directory.FullName, "test/Fixtures/SchemaA/bin", configuration, "net10.0");
    }

    private sealed class TestSettings : IEditorSettingsService
    {
        private readonly EditorSettings _settings = new() { AutoSaveProject = false };
        public EditorSettings GetSettings() => _settings;
        public void SaveSettings() { }
    }

    private sealed class TestDialogs : IFileDialogService
    {
        public string? Folder { get; set; }
        public Task<string?> OpenFolderPickerAsync(string title) => Task.FromResult(Folder);
        public Task<IReadOnlyList<string>> OpenFilePickerAsync(string title, bool allowMultiple = true) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
