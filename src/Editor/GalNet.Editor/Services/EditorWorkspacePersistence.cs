using System.Collections.Generic;
using System.Threading.Tasks;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class EditorWorkspacePersistence
{
    private readonly IProjectService _projects;
    private readonly IEditorDocumentRepository _repository;
    private readonly IEditorDocumentService _documents;
    private readonly IEditorSaveCoordinator _saveCoordinator;
    private readonly EditorGroupProjectionStore? _groups;

    public EditorWorkspacePersistence(
        IProjectService projects,
        IEditorDocumentRepository repository,
        IEditorDocumentService documents,
        IEditorSaveCoordinator saveCoordinator,
        EditorGroupProjectionStore? groups = null)
    {
        _projects = projects;
        _repository = repository;
        _documents = documents;
        _saveCoordinator = saveCoordinator;
        _groups = groups;
    }

    public GraphDocumentLoadResult? LoadCurrentProject()
    {
        if (_projects.Current is not { } project)
        {
            return null;
        }

        if (!_documents.IsLoaded)
            _documents.Load(_repository.Load(project.RootPath, project.Name, project.Settings));
        return GraphDocumentMapper.Load(_documents.LoadedProject, _groups);
    }

    public async Task<bool> SaveCurrentProjectAsync()
    {
        if (_projects.Current is not { } project)
            return false;

        await _projects.SaveAsync();
        return true;
    }

    public string BuildPreviewData(
        string previewPath,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges)
    {
        if (_projects.Current is not { } project)
            throw new InvalidOperationException("No project is currently open.");

        var document = CreateDocument(project.Name, nodes, edges);
        return _saveCoordinator.BuildPreviewData(
            previewPath,
            document,
            GraphDocumentMapper.CreateGroupEntriesSnapshot(nodes));
    }

    private EditorGraphDocument CreateDocument(
        string projectName,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges) =>
        GraphDocumentMapper.CreateDocument(
            projectName,
            _documents.CurrentDocument.Version,
            nodes,
            edges,
            _documents.CurrentDocument.PlayerVariables,
            _documents.CurrentDocument.SaveVariables);
}
