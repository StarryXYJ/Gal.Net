using GalNet.Contracts.Builtins;
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
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Settings;
using GalNet.Editor.Composition;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

[NonParallelizable]
public sealed partial class ProjectWorkspaceLifecycleTests
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
                Assert.That(services.GetRequiredService<IEditorExtensionRegistry>().FindDockPanel("GamePreview"), Is.Null);
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
            var project = await projects.CreateAsync(Path.Combine(_root, "SettingsHistory"), "Original", new DefaultProjectOptions { DefaultHeight = 777 }.ToConfiguration(), Contracts());
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
                Assert.That(project.Configuration.ReadDefaultOptions().SaveSlotCount, Is.EqualTo(19));
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
                Assert.That(project.Configuration.ReadDefaultOptions().DefaultHeight, Is.EqualTo(777));
                await workspace.SaveAsync();
                await projects.CloseAsync();
                var reopened = await projects.OpenAsync(project.RootPath);
                Assert.That(reopened.Name, Is.EqualTo("AfterSave"));
                Assert.That(reopened.Configuration.ReadDefaultOptions().SaveSlotCount, Is.EqualTo(19));
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
        catch (EditorOperationException failure) { Assert.That(failure.Code, Does.StartWith(invalidLocale ? "settings." : "project.")); }
        Assert.That(projects.Current, Is.SameAs(project));
        Assert.That(invalidLocale ? panel.TargetLocale : panel.ProjectName, Is.Empty);
        Assert.That(project.Configuration.ReadDefaultOptions().SaveSlotCount, Is.EqualTo(60), "Invalid input must not partially apply other settings.");
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

    [TestCase("CloseSave", ProjectCloseChoice.Save)]
    [TestCase("CloseDiscard", ProjectCloseChoice.Discard)]
    [TestCase("CloseCancel", ProjectCloseChoice.Cancel)]
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
                else dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Name, buttonLabel))
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
    public async Task VariablesPersistOnlyInTheGraphWhileSettingsRemainIndependent()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Settings"), "Settings", new DefaultProjectOptions { DefaultHeight = 777 }.ToConfiguration(), Contracts());
        var value = new Variable { Name = "score" };
        value.SetValue(4);
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        documents.CurrentDocument.PlayerVariables.Add(new() { Name = "score", DefaultValue = value });
        await projects.SaveAsync();
        using var saved = JsonDocument.Parse(File.ReadAllText(project.SettingsFilePath));
        Assert.That(saved.RootElement.TryGetProperty("playerVariables", out _), Is.False);
        Assert.That(saved.RootElement.TryGetProperty("saveVariables", out _), Is.False);
        using var graph = JsonDocument.Parse(File.ReadAllText(Path.Combine(project.GraphPath, "graph.json")));
        Assert.That(graph.RootElement.GetProperty("playerVariables")[0].GetProperty("name").GetString(), Is.EqualTo("score"));
        Assert.That(project.Configuration.ReadDefaultOptions().DefaultHeight, Is.EqualTo(777));
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
        using var histories = new EditorHistories();
        var store = new EditorGroupProjectionStore(documents, catalog, new EntryFieldAuthoringService(documents, histories));
        var group = store.Get("group");
        var entry = group.Entries.Single();
        entry.ConfigureParameterFields(catalog, [], []);
        var field = (IFieldBinding)entry.ParameterFields.Single();
        new GroupEntryAuthoringService(documents, catalog, histories).Delete("group", [entry.StableId]);
        store.Dispose();
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null);
        Assert.That(entry.Source.Parameters["count"].GetInt32(), Is.EqualTo(4));
    }

    [Test]
    public async Task GraphConnectionsAllowConvergenceAndUndoOnlyTheReplacedOutput()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Convergence"), "Graph", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var first = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var second = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 300, 100);
        var target = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 600, 100);
        workspace.Connect(first.OutputConnectors[0], target.InputConnectors[0]);
        workspace.Connect(second.OutputConnectors[0], target.InputConnectors[0]);
        Assert.That(workspace.Edges.Count(edge => edge.To.Id == target.Id), Is.EqualTo(2));
        var firstEdge = workspace.Edges.Single(edge => edge.From.Id == first.Id).Id;
        workspace.Connect(first.OutputConnectors[0], second.InputConnectors[0]);
        Assert.That(workspace.Edges.Single(edge => edge.From.Id == second.Id).To.Id, Is.EqualTo(target.Id));
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(workspace.Edges.Single(edge => edge.From.Id == first.Id).Id, Is.EqualTo(firstEdge));
        Assert.That(workspace.Edges.Count(edge => edge.To.Id == target.Id), Is.EqualTo(2));
        await workspace.SaveAsync();
        var path = project.RootPath;
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(path);
        var graph = reopened.Services.GetRequiredService<IEditorDocumentService>().CurrentDocument;
        Assert.That(graph.Edges.Count(edge => edge.ToNodeId == target.Id), Is.EqualTo(2));
        await projects.CloseAsync();
    }

    [Test]
    public async Task DeleteGroupRestoresIndependentWorkingAndSavedContentAndHistorySurvivesWorkspaceDisposal()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "DeleteRestore"), "Graph", new(), Contracts());
        var scope = project.Services;
        var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
        entry.Type = "dialogue.text";
        entry.Parameters = new() { ["content"] = "saved" };
        await workspace.SaveAsync();
        entry.Parameters = new() { ["content"] = "working" };
        var source = scope.GetRequiredService<IEditorDocumentService>().Groups[group.Id].RelativePath;
        var path = Path.Combine(project.RootPath, source);
        var savedBytes = File.ReadAllBytes(path);
        await workspace.DeleteNodeAsync(group);
        Assert.That(File.Exists(path), Is.False);
        Assert.That(workspace.Nodes.Any(node => node.Id == group.Id), Is.False);
        workspace.Dispose();
        var replacement = ActivatorUtilities.CreateInstance<EditorWorkspaceViewModel>(scope);
        try
        {
            var histories = scope.GetRequiredService<EditorHistories>();
            await histories.Content.UndoAsync();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(savedBytes));
            var restored = replacement.Nodes.Single(node => node.Id == group.Id);
            Assert.That(restored.Entries.Single().Source.Parameters["content"].GetString(), Is.EqualTo("working"));
            Assert.That(restored.Entries.Single(), Is.SameAs(entry), "Restore must preserve existing Group data bindings.");
            Assert.That(replacement.Edges.Any(edge => edge.To.Id == group.Id), Is.True);
            await replacement.SaveAsync();
            await histories.Content.RedoAsync();
            Assert.That(File.Exists(path), Is.False);
            await histories.Content.UndoAsync();
            Assert.That(File.ReadAllText(path), Does.Contain("working"), "Redo must capture the newer saved baseline.");
            Assert.That(restored.Id, Is.EqualTo(group.Id));
            await histories.Content.UndoAsync();
            Assert.That(replacement.Nodes.Single(node => node.Id == group.Id).Entries.Single().Source.Parameters["content"].GetString(), Is.EqualTo("saved"), "Earlier item edits must still target restored author content.");
        }
        finally { replacement.Dispose(); }
        await projects.CloseAsync();
    }

    [Test]
    public async Task GraphPreviewCancelAndMultiMoveDoNotLeakUncommittedCoordinates()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Move"), "Graph", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var histories = project.Services.GetRequiredService<EditorHistories>();
        var nodes = workspace.Nodes.ToArray();
        var original = nodes.Select(node => (node.Source.X, node.Source.Y)).ToArray();
        foreach (var node in nodes) { node.X += 20; node.Y += 30; }
        Assert.That(content.IsDirty, Is.False);
        Assert.That(histories.Content.CanUndo, Is.False);
        await workspace.SaveAsync();
        Assert.That(content.CaptureSaved().Graph.Nodes.Select(node => (node.X, node.Y)), Is.EqualTo(original));
        workspace.CancelNodeDrag();
        Assert.That(nodes.Select(node => (node.X, node.Y)), Is.EqualTo(original));
        foreach (var node in nodes) { node.X += 20; node.Y += 30; }
        workspace.CommitNodePositions();
        Assert.That(histories.Content.UndoDescription, Is.EqualTo("Move nodes"));
        await histories.Content.UndoAsync();
        Assert.That(nodes.Select(node => (node.X, node.Y)), Is.EqualTo(original));
        Assert.That(histories.Content.CanUndo, Is.False, "One multi-node gesture must create one record.");
        Assert.That(content.IsDirty, Is.False);
        await histories.Content.RedoAsync();
        Assert.That(nodes.All(node => node.Source.X == node.X), Is.True);
        var source = nodes.Last().Source.File;
        nodes.Last().Name = "Display name only";
        Assert.That(nodes.Last().Source.File, Is.EqualTo(source));
        workspace.Dispose();
        await histories.Content.UndoAsync();
        Assert.That(nodes.Last().Source.Name, Is.Not.EqualTo("Display name only"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task GroupCreationWithConnectionIsOneReversibleEmptySourceOperation()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "CreateGroup"), "Graph", new(), Contracts());
        var scope = project.Services;
        var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var entry = workspace.Nodes.Single(node => node.IsEntryNode);
        var oldEdge = workspace.Edges.Single().Source;
        var created = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 400, 200, entry.OutputConnectors[0]);
        var path = Path.Combine(project.RootPath, "Graph", created.Source.File!);
        Assert.That(created.Entries, Is.Empty);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(workspace.Edges.Single().To.Id, Is.EqualTo(created.Id));
        var histories = scope.GetRequiredService<EditorHistories>();
        await histories.Content.UndoAsync();
        Assert.That(File.Exists(path), Is.False);
        Assert.That(workspace.Edges.Single().Id, Is.EqualTo(oldEdge.Id));
        Assert.That(histories.Content.CanUndo, Is.False);
        await histories.Content.RedoAsync();
        Assert.That(workspace.Nodes.Single(node => node.Id == created.Id).Entries, Is.Empty);
        Assert.That(File.Exists(path), Is.True);
        await projects.CloseAsync();
    }

    [Test]
    public async Task GraphDeleteExternalConflictPreservesNodesFilesAndHistory()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "DeleteConflict"), "Graph", new(), Contracts());
        var scope = project.Services;
        var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var path = Path.Combine(project.RootPath, "Graph", group.Source.File!);
        var original = File.ReadAllBytes(path);
        File.AppendAllText(path, " ");
        Exception? failure = null;
        try { await workspace.DeleteNodeAsync(group); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.Not.Null);
        Assert.That(workspace.Nodes, Does.Contain(group));
        Assert.That(File.ReadAllText(path), Does.EndWith(" "));
        Assert.That(scope.GetRequiredService<EditorHistories>().Content.CanUndo, Is.False);
        File.WriteAllBytes(path, original);
        await workspace.DeleteNodeAsync(group);
        Assert.That(File.Exists(path), Is.False);
        await projects.CloseAsync();
    }

    [Test]
    public async Task GraphCanvasActualPointerDragCanCancelAndCommitOneHistoryStep()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PointerGraph"), "Graph", new(), Contracts());
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var view = new NodeGraphPanelView { DataContext = workspace };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            window.Show();
            window.UpdateLayout();
            try
            {
                var node = workspace.Nodes.Single(node => node.IsEntryNode);
                var control = view.GetVisualDescendants().OfType<GalNet.Editor.Controls.GraphNodeControl>().Single(control => ReferenceEquals(control.DataContext, node));
                using var initialFrame = window.CaptureRenderedFrame();
                var label = control.GetVisualDescendants().OfType<TextBlock>().First(label => label.Text == node.Name);
                var point = label.TranslatePoint(new Point(10, 8), window)!.Value;
                var original = (node.X, node.Y);
                window.MouseDown(point, MouseButton.Left);
                window.MouseMove(point + new Vector(45, 30), RawInputModifiers.LeftMouseButton);
                Assert.That((node.X, node.Y), Is.Not.EqualTo(original), "Actual pointer movement must reach the canvas.");
                Assert.That((node.Source.X, node.Source.Y), Is.EqualTo(original));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(point + new Vector(45, 30), MouseButton.Left);
                Assert.That((node.X, node.Y), Is.EqualTo(original));
                Assert.That(workspace.UndoRedoHistory.CanUndo, Is.False);
                window.UpdateLayout();
                window.MouseDown(point, MouseButton.Left);
                window.MouseMove(point + new Vector(50, 35), RawInputModifiers.LeftMouseButton);
                window.MouseUp(point + new Vector(50, 35), MouseButton.Left);
                Assert.That(node.Source.X, Is.EqualTo(node.X));
                Assert.That((node.Source.X, node.Source.Y), Is.Not.EqualTo(original));
                await workspace.UndoRedoHistory.UndoAsync();
                Assert.That((node.X, node.Y), Is.EqualTo(original));
                Assert.That(workspace.UndoRedoHistory.CanUndo, Is.False);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                {
                    window.UpdateLayout();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(artifacts, "p2-graph-canvas.png"));
                }
            }
            finally { window.Close(); }
            await projects.CloseAsync();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task BranchCreationRemainsUnsavedUntilSaveAndCanBeUndoneWithoutFilePublication()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Branch"), "Graph", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var bytes = File.ReadAllBytes(Path.Combine(project.RootPath, "Graph/graph.json"));
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 200);
        Assert.That(branch.Options.Single().Id, Is.Not.Empty);
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "Graph/graph.json")), Is.EqualTo(bytes));
        Assert.That(content.IsDirty, Is.True);
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(content.IsDirty, Is.False);
        await workspace.UndoRedoHistory.RedoAsync();
        await workspace.SaveAsync();
        Assert.That(content.CaptureSaved().Graph.Nodes.Any(node => node.Id == branch.Id), Is.True);
        await projects.CloseAsync();
    }

    [Test]
    public async Task DeletingMultipleGroupsRestoresTheirOrderConnectionsAndSourcesInOneStep()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "DeleteSelection"), "Graph", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var first = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var second = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 600, 200, first.OutputConnectors[0]);
        var ids = workspace.Nodes.Select(node => node.Id).ToArray();
        var edges = workspace.Edges.Select(edge => edge.Id).ToArray();
        var histories = project.Services.GetRequiredService<EditorHistories>();
        histories.Clear();
        workspace.SelectNodes([first, second]);
        await workspace.DeleteSelectionAsync();
        Assert.That(workspace.Nodes.Single().IsEntryNode, Is.True);
        Assert.That(workspace.SelectedNodes, Is.Empty);
        await histories.Content.UndoAsync();
        Assert.That(workspace.Nodes.Select(node => node.Id), Is.EqualTo(ids));
        Assert.That(workspace.Edges.Select(edge => edge.Id), Is.EqualTo(edges));
        Assert.That(histories.Content.CanUndo, Is.False);
        Assert.That(File.Exists(Path.Combine(project.RootPath, "Graph", first.Source.File!)), Is.True);
        Assert.That(File.Exists(Path.Combine(project.RootPath, "Graph", second.Source.File!)), Is.True);
        await projects.CloseAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task QueuedCreationRevalidatesItsStartingNodeAndOutletBeforeWriting(bool removeNode)
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "QueuedCreate"), "Graph", new(), Contracts());
        var scope = project.Services;
        var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 200);
        branch.Options.Add(new() { Id = Guid.NewGuid().ToString("N"), Text = "Second" });
        branch.RefreshConnectors();
        var release = new TaskCompletionSource();
        var history = scope.GetRequiredService<EditorHistories>().Content;
        var documents = scope.GetRequiredService<IEditorDocumentService>();
        var blocker = history.ExecuteAsync(new CallbackEdit("Previous queued change", async () =>
        {
            await release.Task;
            if (removeNode) documents.CurrentDocument.Nodes.Remove(branch.Source);
            else branch.Source.Options!.RemoveAt(1);
            documents.SynchronizeGroups();
        }, () => ValueTask.CompletedTask));
        var files = Directory.GetFiles(Path.Combine(project.RootPath, "Graph/groups"));
        var creating = workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 600, 200, branch.OutputConnectors[1]);
        release.SetResult();
        await blocker;
        Exception? failure = null;
        try { await creating; } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.Not.Null);
        Assert.That(Directory.GetFiles(Path.Combine(project.RootPath, "Graph/groups")), Is.EquivalentTo(files));
        Assert.That(workspace.Nodes.Count(node => node.NodeKind == GraphNodeKind.LinearGroup), Is.EqualTo(1));
        Assert.That(history.UndoDescription, Is.EqualTo("Previous queued change"));
        await projects.CloseAsync();
    }

    private static ServiceProvider Services(IProjectClosePrompt? closePrompt = null, IEditorLocalizationService? localization = null, GalNet.Editor.Shared.Commands.AuthorFileTransaction? transaction = null, IEntryCatalog? catalog = null)
    {
        var services = new ServiceCollection().AddEditorServices()
            .AddSingleton<IEditorLocalizationService>(localization ?? new EditorLocalizationService())
            .AddSingleton<IProjectClosePrompt>(closePrompt ?? new DiscardProjectClosePrompt())
            .AddSingleton<IEditorSettingsService>(new TestSettings())
            .AddSingleton<IFileDialogService>(new TestDialogs())
            .AddSingleton<IEffectCatalog>(new EffectCatalog([]));
        if (transaction is not null)
            services.AddScoped<IEditorDocumentRepository>(provider => new FaultRepository(new EditorDocumentRepository(provider.GetRequiredService<IEntryCatalog>(), EditorDocumentValidation.Authoring), transaction));
        if (catalog is not null) services.AddScoped<IEntryCatalog>(_ => catalog);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

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
        public string? SavePath { get; set; }
        public IReadOnlyList<string> OpenPaths { get; set; } = [];
        public Task<string?> SaveFilePickerAsync(string title, string suggestedName) => Task.FromResult(SavePath);
        public Task<string?> OpenFolderPickerAsync(string title) => Task.FromResult(Folder);
        public Task<IReadOnlyList<string>> OpenFilePickerAsync(string title, bool allowMultiple = true) => Task.FromResult(OpenPaths);
    }
}
