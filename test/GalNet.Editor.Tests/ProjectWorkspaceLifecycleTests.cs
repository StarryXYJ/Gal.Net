using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
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
    public async Task SettingsViewCommitsOneEditAndHistorySurvivesClosingThePage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "SettingsHistory"), "Original", new() { Speakers = ["keep speaker"] }, Contracts());
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var view = new ProjectSettingsPanelView { DataContext = panel };
            var editorPage = new EditorPageView { DataContext = project.Services.GetRequiredService<EditorPageViewModel>(), Content = view };
            var window = new Window { Width = 960, Height = 700, Content = editorPage };
            window.Show();
            window.UpdateLayout();
            try
            {
                var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
                group.Name = "Local undo sentinel";
                var input = view.FindControl<TextBox>("ProjectNameInput")!;
                input.Focus();
                input.SelectAll();
                window.KeyTextInput("typing a draft");
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
                window.KeyRelease(Key.Z, RawInputModifiers.None, PhysicalKey.Z, "z");
                Assert.That(input.Text, Is.EqualTo("Original"));
                Assert.That(group.Name, Is.EqualTo("Local undo sentinel"), "Local text undo must consume the shortcut before the project history.");
                input.SelectAll();
                window.KeyTextInput("Renamed");
                panel.SaveSlotCount = 19;
                Assert.That(project.Name, Is.EqualTo("Original"), "Typing stays in the page draft until explicit commit.");
                Assert.That(await projects.CheckUnsavedChangesAsync(), Is.True);
                await ((IAsyncRelayCommand)view.FindControl<Button>("ApplySettingsButton")!.Command!).ExecuteAsync(null);
                Assert.That(input.CanUndo, Is.False, "Committed text must not merge with an earlier local input gesture.");
                window.UpdateLayout();
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                {
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(artifacts, "p1b-project-settings.png"));
                }
                Assert.That(project.Name, Is.EqualTo("Renamed"));
                Assert.That(project.Settings.SaveSlotCount, Is.EqualTo(19));
                await workspace.SaveAsync();
                panel.ProjectName = "AfterSave";
                await panel.SaveSettingsCommand.ExecuteAsync(null);
                panel.Dispose();
                var router = project.Services.GetRequiredService<UndoRedoRouter>();
                router.SetActive(null);
                await router.UndoAsync();
                Assert.That(project.Name, Is.EqualTo("Renamed"));
                Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
                await router.RedoAsync();
                Assert.That(project.Name, Is.EqualTo("AfterSave"));
                Assert.That(project.Settings.Speakers.Single(), Is.EqualTo("keep speaker"));
                await workspace.SaveAsync();
                await projects.CloseAsync();
                var reopened = await projects.OpenAsync(project.RootPath);
                Assert.That(reopened.Name, Is.EqualTo("AfterSave"));
                Assert.That(reopened.Settings.SaveSlotCount, Is.EqualTo(19));
                await projects.CloseAsync();
            }
            finally { panel.Dispose(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [TestCase(ProjectCloseChoice.Save)]
    [TestCase(ProjectCloseChoice.Discard)]
    [TestCase(ProjectCloseChoice.Cancel)]
    public async Task CloseProjectHonorsSaveDiscardAndCancel(ProjectCloseChoice choice)
    {
        var prompt = new TestClosePrompt(choice);
        using var services = Services(prompt);
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "CloseChoice"), "Before", new(), Contracts());
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        documents.CurrentDocument.Name = "Changed";
        try
        {
            await projects.CloseAsync();
            Assert.That(choice, Is.Not.EqualTo(ProjectCloseChoice.Cancel));
        }
        catch (OperationCanceledException) { Assert.That(choice, Is.EqualTo(ProjectCloseChoice.Cancel)); }
        Assert.That(prompt.Calls, Is.EqualTo(1));
        if (choice == ProjectCloseChoice.Cancel)
        {
            Assert.That(projects.Current, Is.SameAs(project));
            Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.True);
            prompt.Choice = ProjectCloseChoice.Discard;
            await projects.CloseAsync();
        }
        Assert.That(projects.Current, Is.Null);
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Name, Is.EqualTo(choice == ProjectCloseChoice.Save ? "Changed" : "Before"));
        await projects.CloseAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidSettingsDraftBlocksSaveAndCloseWithoutDiscardingTheInput(bool invalidLocale)
    {
        using var services = Services(new TestClosePrompt(ProjectCloseChoice.Save));
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "InvalidDraft"), "Before", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
        if (invalidLocale) panel.TargetLocale = "";
        else panel.ProjectName = "";
        panel.SaveSlotCount = 19;
        try { await projects.CloseAsync(); Assert.Fail("An invalid draft must block Save and Close."); }
        catch (InvalidDataException) { }
        Assert.That(projects.Current, Is.SameAs(project));
        Assert.That(invalidLocale ? panel.TargetLocale : panel.ProjectName, Is.Empty);
        Assert.That(project.Settings.SaveSlotCount, Is.EqualTo(60), "Invalid input must not partially apply other settings.");
        Assert.That(panel.IsDirtyText, Is.Not.Empty);
        panel.CancelDraftCommand.Execute(null);
        Assert.That(panel.ProjectName, Is.EqualTo("Before"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task DiscardCloseKeepsACompleteRecoverySnapshotWhileWorkspaceDetaches()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ClosingRecovery"), "Before", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
        entry.Type = "dialogue.text";
        entry.Parameters = new() { ["content"] = "keep in recovery" };
        await projects.CloseAsync();
        var snapshot = Directory.GetFiles(Path.Combine(project.RootPath, ".galnet/editor/recovery"), "*.json").Single();
        var recovered = ProjectRecoveryStore.Read(snapshot);
        Assert.That(recovered.Graph.Nodes, Has.Count.EqualTo(2));
        Assert.That(recovered.GroupEntries[group.Id].Single().Parameters["content"].GetString(), Is.EqualTo("keep in recovery"));
    }

    [Test]
    public async Task SaveWaitsForTheRunningEditWithoutDeadlockingItsStructuralPublication()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "SaveOrdering"), "Before", new(), Contracts());
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var group = content.CaptureWorking().Graph.Nodes.Single(node => node.Type == "Group");
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var editing = history.ExecuteAsync(new CallbackEdit("Move Group", async () =>
        {
            started.SetResult();
            await release.Task;
            await content.PublishStructureAsync(new RenameSource(group.Id, "groups/queued.rawgalgroup"));
        }, async () => await content.PublishStructureAsync(new RenameSource(group.Id, group.File!))));
        await started.Task;
        var saving = projects.SaveAsync();
        Assert.That(saving.IsCompleted, Is.False);
        release.SetResult();
        await Task.WhenAll(editing, saving).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(File.Exists(Path.Combine(project.RootPath, "Graph/groups/queued.rawgalgroup")), Is.True);
        Assert.That(content.IsDirty, Is.False);
        await history.UndoAsync();
        Assert.That(File.Exists(Path.Combine(project.RootPath, "Graph", group.File!)), Is.True);
        Assert.That(content.IsDirty, Is.False);
        await projects.CloseAsync();
    }

    private sealed record RenameSource(string GroupId, string File) : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version) => document.Graph.Nodes.Single(node => node.Id == GroupId).File = File;
    }

    private sealed record CallbackEdit(string Description, Func<ValueTask> RedoAction, Func<ValueTask> UndoAction) : IAsyncUndoableEdit
    {
        public ValueTask RedoAsync() => RedoAction();
        public ValueTask UndoAsync() => UndoAction();
    }

    [Test]
    public async Task CreatingTheWorkspaceAfterASettingsEditDoesNotResetProjectHistory()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "LateWorkspace"), "Before", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
        panel.ProjectName = "Edited before opening Graph";
        panel.TargetLocale = "zh_cn";
        await panel.SaveSettingsCommand.ExecuteAsync(null);
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        Assert.That(workspace.UndoRedoHistory.CanUndo, Is.True);
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(project.Name, Is.EqualTo("Before"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task HistoryCompensationFailureBlocksSaveAndRetainsTheAuthorSnapshot()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "HistoryRepair"), "Before", new(), Contracts());
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        Assert.Throws<HistoryRepairRequiredException>(() => history.Execute(new CompositeEdit("failed composite",
        [
            new DelegateEdit("first", () => throw new IOException("cannot compensate"), () => documents.CurrentDocument.Name = "partially changed"),
            new DelegateEdit("second", () => { }, () => throw new IOException("cannot apply"))
        ])));
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        Assert.That(content.RepairRequired, Is.True);
        try { await projects.SaveAsync(); Assert.Fail("RepairRequired must block Save All."); }
        catch (InvalidOperationException) { }
        Assert.That(Directory.GetFiles(Path.Combine(project.RootPath, ".galnet/editor/recovery"), "*.json"), Is.Not.Empty);
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Name, Is.EqualTo("Before"));
        await projects.CloseAsync();
    }

    private sealed class TestClosePrompt(ProjectCloseChoice choice) : IProjectClosePrompt
    {
        public ProjectCloseChoice Choice { get; set; } = choice;
        public int Calls { get; private set; }
        public Task<ProjectCloseChoice> ChooseAsync(string projectName) { Calls++; return Task.FromResult(Choice); }
    }

    [TestCase("Save All", ProjectCloseChoice.Save)]
    [TestCase("Discard Unsaved Content", ProjectCloseChoice.Discard)]
    [TestCase("Cancel", ProjectCloseChoice.Cancel)]
    [TestCase(null, ProjectCloseChoice.Cancel)]
    public async Task CloseDialogButtonsAndWindowCloseReturnTheExpectedChoice(string? buttonLabel, ProjectCloseChoice expected)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            var owner = new Window();
            owner.Show();
            var dialog = ProjectClosePrompt.CreateDialog("Example");
            try
            {
                var result = dialog.ShowDialog<ProjectCloseChoice>(owner);
                dialog.UpdateLayout();
                if (buttonLabel is null) dialog.Close();
                else dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, buttonLabel))
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.That(await result, Is.EqualTo(expected));
            }
            finally { dialog.Close(); owner.Close(); }
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

    private static ServiceProvider Services(IProjectClosePrompt? closePrompt = null) => new ServiceCollection().AddEditorServices()
        .AddSingleton<IProjectClosePrompt>(closePrompt ?? new DiscardProjectClosePrompt())
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

    private sealed class DiscardProjectClosePrompt : IProjectClosePrompt
    {
        public Task<ProjectCloseChoice> ChooseAsync(string projectName) => Task.FromResult(ProjectCloseChoice.Discard);
    }

    private sealed class TestDialogs : IFileDialogService
    {
        public string? Folder { get; set; }
        public Task<string?> OpenFolderPickerAsync(string title) => Task.FromResult(Folder);
        public Task<IReadOnlyList<string>> OpenFilePickerAsync(string title, bool allowMultiple = true) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
