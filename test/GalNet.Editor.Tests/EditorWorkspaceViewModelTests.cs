using System.ComponentModel;
using System.Globalization;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Primitives.Builtins;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

[TestFixture]
public sealed class EditorWorkspaceViewModelTests
{
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
    public void GraphEdit_UndoAndRedoRestoreTheSameNode()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = fixture.Workspace;
        var initialCount = workspace.Nodes.Count;

        var added = workspace.AddNode(GraphNodeKind.LinearGroup, 120, 240);

        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount + 1));
        Assert.That(workspace.UndoRedoHistory.CanUndo, Is.True);

        workspace.UndoRedoHistory.Undo();
        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount));
        Assert.That(workspace.Nodes, Does.Not.Contain(added));

        workspace.UndoRedoHistory.Redo();
        Assert.That(workspace.Nodes, Has.Count.EqualTo(initialCount + 1));
        Assert.That(workspace.Nodes, Does.Contain(added));
    }

    [Test]
    public async Task SaveAsync_WritesCurrentGraphAndMarksAllCheckpointsSaved()
    {
        using var fixture = new WorkspaceFixture(withProject: true);
        var workspace = fixture.Workspace;
        workspace.AddNode(GraphNodeKind.LinearGroup, 120, 240);

        await workspace.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.SaveCoordinator.SavedDocument, Is.Not.Null);
            Assert.That(fixture.SaveCoordinator.SavedDocument!.Nodes, Has.Count.EqualTo(workspace.Nodes.Count));
            Assert.That(fixture.ProjectService.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.SaveScheduler.SaveNowCount, Is.EqualTo(1));
            Assert.That(fixture.DocumentService.IsDirty, Is.False);
            Assert.That(workspace.UndoRedoHistory.IsDirty, Is.False);
            Assert.That(fixture.ProjectService.Current!.IsDirty, Is.False);
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
            saveCoordinator,
            new GraphDocumentMapper());

        var graph = persistence.LoadCurrentProject();
        var saved = await persistence.SaveCurrentProjectAsync(graph!.Nodes, graph.Edges);

        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes, Has.Count.EqualTo(2));
            Assert.That(documents.CurrentDocument.Name, Is.EqualTo("Test Project"));
            Assert.That(saved, Is.True);
            Assert.That(saveCoordinator.SavedDocument, Is.Not.Null);
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

        public WorkspaceFixture(bool withProject = false)
        {
            var loaded = CreateLoadedDocument();
            if (withProject)
            {
                var projectScope = new FakeScope();
                ProjectService.SetCurrent(new GalProject(
                    "test",
                    "Test Project",
                    "C:\\test-project",
                    new ProjectSettings(),
                    new EditorProjectState(),
                    projectScope));
            }

            var localization = new FakeLocalizationService();
            var dockFactory = new EditorDockFactory(
                new NullServiceProvider(),
                new EditorExtensionRegistry(),
                localization);
            var catalog = BuiltinEntryModules.CreateRecommendedTargetProfile();
            var variableDefinitions = new VariableDefinitionService(DocumentService);
            var repository = new FakeDocumentRepository(loaded);
            var mapper = new GraphDocumentMapper();
            var persistence = new EditorWorkspacePersistence(
                ProjectService,
                repository,
                DocumentService,
                SaveCoordinator,
                mapper);

            Workspace = new EditorWorkspaceViewModel(
                ProjectService,
                new EditorHistories(),
                dockFactory,
                DocumentService,
                variableDefinitions,
                new GraphEditingService(catalog),
                new FakeEditorSettingsService(),
                localization,
                SaveScheduler,
                new GraphSelectionState(),
                persistence);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            ProjectService.Current?.Dispose();
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
                    new EditorGraphNodeDto { Id = "opening", Type = "Group", Name = "Opening" }
                ],
                Edges = [new EditorGraphEdgeDto { Id = "edge", FromNodeId = "entry", ToNodeId = "opening" }]
            }
        };
    }

    private sealed class FakeProjectService : IProjectService
    {
        public GalProject? Current { get; private set; }
        public int SaveCount { get; private set; }
        public event Action<GalProject?>? CurrentChanged { add { } remove { } }

        public void SetCurrent(GalProject project) => Current = project;
        public Task<GalProject> OpenAsync(string projectPath) => throw new NotSupportedException();
        public Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings) => throw new NotSupportedException();
        public Task CloseAsync() => Task.CompletedTask;
        public Task SaveAsync() { SaveCount++; return Task.CompletedTask; }
        public IReadOnlyList<RecentProjectInfo> GetRecentProjects() => [];
        public void RemoveRecentProject(string projectPath) { }
        public Task<bool> CheckUnsavedChangesAsync() => Task.FromResult(Current?.IsDirty == true);
    }

    private sealed class FakeDocumentRepository(LoadedEditorProjectDocument loaded) : IEditorDocumentRepository
    {
        public LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings) => loaded;
        public void Save(string projectPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries) => throw new NotSupportedException();
    }

    private sealed class CapturingSaveCoordinator : IEditorSaveCoordinator
    {
        public EditorGraphDocument? SavedDocument { get; private set; }

        public void SaveProjectDocument(string projectPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries) => SavedDocument = document;
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
