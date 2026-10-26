using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using GalNet.Primitives.Builtins;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

[TestFixture]
public sealed class EditorWorkspaceViewModelTests
{
    [TestCase(false), TestCase(true), NonParallelizable]
    public async Task SchemaFieldCommitUsesWorkspaceHistoryAndSaveSnapshot(bool useDto)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var fixture = new WorkspaceFixture(withProject: true, useDto: useDto);
            var workspace = fixture.Workspace;
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
            entry.Type = BuiltinEntryIds.Wait;
            entry.Parameters = new() { ["duration"] = "1" };
            if (useDto) entry.Parameters = new(entry.Parameters) { ["label"] = "wait" };
            var catalog = fixture.Catalog;
            entry.ConfigureParameterFields(catalog, [], []);
            // Supply only layout icon resources; the real parameter panel and bindings are used below.
            foreach (var key in new[] { "AddIcon", "DeleteRegularIcon", "ChevronUpRegular", "ChevronDownRegular" })
                Application.Current!.Resources[key] = StreamGeometry.Parse("M0,0 L8,8");
            var panel = new GroupEditorPanelViewModel(workspace, group.Id, fixture.EntryAuthoring, fixture.ProjectService,
                null!, null!, null!, catalog, new GalNet.Editor.Services.EntrySelectionContext());
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Application.Current.Styles.Add(new Semi.Avalonia.SemiTheme());
            var view = new EntryListEditorControl { DataContext = panel };
            var window = new Window { Width = 900, Height = 500, Content = view };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            try
            {
                var forms = view.GetVisualDescendants().OfType<SchemaForm>().Where(item => item.Schema is { Count: > 0 }).ToArray();
                Assert.That(forms.Length, Is.EqualTo(1));
                var form = forms.Single();
                if (useDto)
                {
                    entry.ConfigureParameterFields(catalog, [], []);
                    window.UpdateLayout();
                    form = view.GetVisualDescendants().OfType<SchemaForm>().Single(item => item.Schema is { Count: > 0 });
                    Assert.That(form.Contexts[1].Text, Is.EqualTo("wait"));
                }
                Assert.That(form.Diagnostics, Is.Empty);
                Assert.That(form.Contexts[0].Field.Id, Is.EqualTo("duration"));
                Assert.That(view.GetVisualDescendants(), Does.Contain(form.Editors[0].Control));
                var field = useDto ? entry.ParameterForm!.Binding(entry.ParameterForm.Fields[0]) : (IFieldBinding)entry.ParameterFields.Single();
                if (useDto) Assert.That(entry.ParameterFields, Is.Empty, "The generated DTO replaces the old per-field projection.");
                var changes = 0;
                field.Changed += (_, _) => changes++;
                await fixture.Content.SaveAsync();
                ((TextBox)form.Editors[0].Control).Text = "1.5";
                Assert.That(await form.Contexts[0].CommitTextAsync(), Is.True);
                Assert.That(workspace.UndoRedoHistory.IsDirty, Is.True);
                workspace.UndoRedoHistory.Undo();
                Assert.That(field.Read().Json!.Value.GetSingle(), Is.EqualTo(1));
                Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("1"));
                Assert.That(workspace.UndoRedoHistory.IsDirty, Is.False);
                workspace.UndoRedoHistory.Redo();
                Assert.That(field.Read().Json!.Value.GetSingle(), Is.EqualTo(1.5));
                Assert.That(changes, Is.EqualTo(3));
                await workspace.SaveAsync();
                Assert.That(entry.Source.Parameters["duration"].GetSingle(), Is.EqualTo(1.5f));
                Assert.That(fixture.ProjectService.SaveCount, Is.EqualTo(1));
                Assert.That(workspace.UndoRedoHistory.IsDirty, Is.False);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public void Selection_KeepsNodeAndEdgeFlagsConsistent()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = fixture.Workspace;
        var first = workspace.Nodes[1];
        var second = workspace.Nodes[2];
        var edge = workspace.Edges[0];

        workspace.SelectNode(first);
        workspace.SelectNode(second, additive: true);

        Assert.Multiple(() =>
        {
            Assert.That(workspace.SelectedNodes, Is.EqualTo(new[] { first, second }));
            Assert.That(workspace.SelectedNode, Is.Null);
            Assert.That(workspace.HasMultipleNodeSelection, Is.True);
            Assert.That(first.IsSelected, Is.True);
            Assert.That(second.IsSelected, Is.True);
        });

        workspace.SelectEdge(edge);

        Assert.Multiple(() =>
        {
            Assert.That(workspace.SelectedNodes, Is.Empty);
            Assert.That(workspace.SelectedEdge, Is.SameAs(edge));
            Assert.That(edge.IsSelected, Is.True);
            Assert.That(first.IsSelected, Is.False);
            Assert.That(second.IsSelected, Is.False);
        });

        workspace.ClearSelection();

        Assert.Multiple(() =>
        {
            Assert.That(workspace.SelectedEdge, Is.Null);
            Assert.That(edge.IsSelected, Is.False);
            Assert.That(workspace.HasMultipleNodeSelection, Is.False);
        });
    }

    [Test]
    public async Task GraphEdit_UndoAndRedoRestoreTheStableNodeIdentity()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = fixture.Workspace;
        var initialCount = workspace.Nodes.Count;

        var added = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 120, 240);

        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount + 1));
        Assert.That(workspace.UndoRedoHistory.CanUndo, Is.True);

        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount));
        Assert.That(workspace.Nodes, Does.Not.Contain(added));

        await workspace.UndoRedoHistory.RedoAsync();
        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount + 1));
        Assert.That(workspace.Nodes.Select(node => node.Id), Does.Contain(added.Id));
    }

    [Test]
    public async Task SaveAsync_WritesCurrentGraphAndMarksAllCheckpointsSaved()
    {
        using var fixture = new WorkspaceFixture(withProject: true);
        var workspace = fixture.Workspace;
        await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 120, 240);

        await workspace.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.ProjectService.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.SaveScheduler.SaveNowCount, Is.EqualTo(1));
            Assert.That(fixture.Content.IsDirty, Is.False);
            Assert.That(workspace.UndoRedoHistory.IsDirty, Is.False);
            Assert.That(fixture.ProjectService.Current, Is.Not.Null);
        });
    }

    [Test]
    public void GraphSelectionState_ManagesSelectionWithoutAWorkspace()
    {
        var selection = new GraphSelectionState();
        var first = new GraphNode(new GalNet.Core.Graph.Group { Name = "First" }, GraphNodeKind.LinearGroup);
        var second = new GraphNode(new GalNet.Core.Graph.Group { Name = "Second" }, GraphNodeKind.LinearGroup);

        selection.SelectNodes([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(selection.SelectedNodes, Is.EqualTo(new[] { first, second }));
            Assert.That(selection.SelectedNode, Is.Null);
            Assert.That(selection.HasMultipleNodes, Is.True);
            Assert.That(first.IsSelected, Is.True);
            Assert.That(second.IsSelected, Is.True);
        });

        selection.Clear();
        Assert.That(selection.SelectedNodes, Is.Empty);
        Assert.That(first.IsSelected, Is.False);
        Assert.That(second.IsSelected, Is.False);
    }

    [Test]
    public async Task EditorWorkspacePersistence_LoadsAndSavesWithoutAWorkspace()
    {
        var projects = new FakeProjectService();
        projects.SetCurrent(new GalProject(
            "test",
            "Test Project",
            "C:\\test-project",
            new ProjectSettings(),
            new EditorProjectState(),
            new FakeScope()));
        var documents = new EditorDocumentService();
        var saveCoordinator = new CapturingSaveCoordinator();
        var persistence = new EditorWorkspacePersistence(
            projects,
            new FakeDocumentRepository(WorkspaceFixture.CreateLoadedDocument()),
            documents,
            saveCoordinator);

        var graph = persistence.LoadCurrentProject()!;
        var saved = await persistence.SaveCurrentProjectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes, Has.Count.EqualTo(2));
            Assert.That(documents.CurrentDocument.Name, Is.EqualTo("Test Project"));
            Assert.That(saved, Is.True);
            Assert.That(projects.SaveCount, Is.EqualTo(1));
        });

        projects.Current!.Dispose();
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public FakeProjectService ProjectService { get; } = new();
        public EditorDocumentService DocumentService { get; } = new();
        public CapturingSaveCoordinator SaveCoordinator { get; } = new();
        public ImmediateSaveScheduler SaveScheduler { get; } = new();
        public EditorWorkspaceViewModel Workspace { get; }
        public IEntryCatalog Catalog { get; }
        public GroupEntryAuthoringService EntryAuthoring { get; }

        private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-workspace-test-" + Guid.NewGuid().ToString("N"))).FullName;
        private readonly ProjectContentSession _content;
        public ProjectContentSession Content => _content;

        public WorkspaceFixture(bool withProject = false, bool useDto = false)
        {
            var loaded = CreateLoadedDocument();
            if (!withProject)
                loaded.Document.Nodes.Add(new EditorGraphNodeDto { Id = "choice", Type = "Branch", BranchType = "Choice", Name = "Choice" });
            {
                var projectScope = new FakeScope();
                ProjectService.SetCurrent(new GalProject(
                    "test",
                    "Test Project",
                    _root,
                    new ProjectSettings(),
                    new EditorProjectState(),
                    projectScope));
            }

            var localization = new FakeLocalizationService();
            var dockFactory = new EditorDockFactory(
                new NullServiceProvider(),
                new EditorExtensionRegistry(),
                localization);
            var modules = BuiltinSchemas.CreateAllModules().Where(module => module.Id != "flow").ToList();
            var wait = useDto ? DefaultPrimitiveEntryBase.ForDto<EditorWaitOptions>(BuiltinEntryIds.Wait)
                : new DefaultPrimitiveEntryBase(BuiltinEntryIds.Wait, new GalNet.Core.Primitives.DynamicParameterTable(
                    [new GalNet.Core.Primitives.DynamicParameterDescriptor("duration", typeof(float),
                        defaultValue: System.Text.Json.JsonSerializer.SerializeToElement(1f), editor: new(ValueEditorIds.Numeric))]));
            modules.Add(new DefaultEntryModule("flow", [wait]));
            var catalog = new TargetProfileEntryCatalog(modules);
            Catalog = catalog;
            var variableDefinitions = new VariableDefinitionService(DocumentService);
            var repository = new FakeDocumentRepository(loaded);
            DocumentService.Load(loaded);
            File.WriteAllText(Path.Combine(_root, "settings.json"), System.Text.Json.JsonSerializer.Serialize(ProjectService.Current!.Settings));
            _content = new ProjectContentSession(DocumentService, repository);
            _content.Open(ProjectService.Current!);
            ProjectService.Content = _content;
            var histories = new EditorHistories(_content);
            var groups = new EditorGroupProjectionStore(DocumentService, catalog, new EntryFieldAuthoringService(DocumentService, histories));
            var persistence = new EditorWorkspacePersistence(ProjectService, repository, DocumentService, SaveCoordinator, groups);
            EntryAuthoring = new GroupEntryAuthoringService(groups, DocumentService, catalog, histories);
            Workspace = new EditorWorkspaceViewModel(
                ProjectService,
                histories,
                dockFactory,
                DocumentService,
                variableDefinitions,
                new GraphEditingService(),
                new FakeEditorSettingsService(),
                localization,
                SaveScheduler,
                new GraphSelectionState(),
                persistence, new GraphAuthoringService(DocumentService, histories, _content), EntryAuthoring);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            EntryAuthoring.Dispose();
            _content.Dispose();
            ProjectService.Current?.Dispose();
            Directory.Delete(_root, true);
        }

        public static LoadedEditorProjectDocument CreateLoadedDocument() => new()
        {
            Document = new EditorGraphDocument
            {
                Version = 2,
                Name = "Test Project",
                RootNodeId = "entry",
                Nodes =
                [
                    new EditorGraphNodeDto { Id = "entry", Type = "Entry", Name = "Entry" },
                    new EditorGraphNodeDto { Id = "opening", Type = "Group", Name = "Opening", File = "groups/opening.rawgalgroup" }
                ],
                Edges = [new EditorGraphEdgeDto { Id = "edge", FromNodeId = "entry", ToNodeId = "opening" }]
            }
        };
    }

    private sealed class FakeProjectService : IProjectService
    {
        public GalProject? Current { get; private set; }
        public int SaveCount { get; private set; }
        public IProjectContentSession? Content { get; set; }
        public event Action<GalProject?>? CurrentChanged { add { } remove { } }

        public void SetCurrent(GalProject project) => Current = project;
        public Task<GalProject> OpenAsync(string projectPath) => throw new NotSupportedException();
        public Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings, string contractsDirectory) => throw new NotSupportedException();
        public Task CloseAsync() => Task.CompletedTask;
        public Task SaveAsync() { SaveCount++; return Content?.SaveAsync() ?? Task.CompletedTask; }
        public IReadOnlyList<RecentProjectInfo> GetRecentProjects() => [];
        public void RemoveRecentProject(string projectPath) { }
        public Task<bool> CheckUnsavedChangesAsync() => Task.FromResult(Content?.IsDirty == true);
    }

    private sealed class FakeDocumentRepository(LoadedEditorProjectDocument loaded) : IEditorDocumentRepository
    {
        public LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings) => loaded;
        public ProjectSavePlan PrepareSave(string projectPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries) => new(new Dictionary<string, byte[]>(), []);
        public void ApplySave(string projectPath, ProjectSavePlan plan, CancellationToken cancellationToken = default)
        {
            foreach (var pair in plan.Writes)
            {
                var path = Path.Combine(projectPath, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, pair.Value);
            }
        }
        public void Save(string projectPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries) => throw new NotSupportedException();
    }

    private sealed class CapturingSaveCoordinator : IEditorSaveCoordinator
    {
        public string BuildPreviewData(string previewPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries) => previewPath;
    }

    private sealed class ImmediateSaveScheduler : IProjectSaveScheduler
    {
        public int SaveNowCount { get; private set; }
        public void Schedule(Func<Task> save) { }
        public async Task SaveNowAsync(Func<Task> save) { SaveNowCount++; await save(); }
    }

    private sealed class FakeEditorSettingsService : IEditorSettingsService
    {
        private readonly EditorSettings _settings = new() { AutoSaveProject = false };
        public EditorSettings GetSettings() => _settings;
        public void SaveSettings() { }
    }

    private sealed class FakeLocalizationService : IEditorLocalizationService
    {
        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
        public IReadOnlyList<CultureInfo> AvailableCultures => [CurrentCulture];
        public string this[string key] => key;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, key, args);
        public void ApplyLocale(string localeCode) { }
        public void ApplyCulture(CultureInfo culture) { }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new NullServiceProvider();
        public void Dispose() { }
    }
}
