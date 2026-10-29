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
    private readonly EditorGroupProjectionStore? _groups;

    public EditorWorkspacePersistence(
        IProjectService projects,
        IEditorDocumentRepository repository,
        IEditorDocumentService documents,
        EditorGroupProjectionStore? groups = null)
    {
        _projects = projects;
        _repository = repository;
        _documents = documents;
        _groups = groups;
    }

    public GraphDocumentLoadResult? LoadCurrentProject()
    {
        if (_projects.Current is not { } project)
        {
            return null;
        }

        if (!_documents.IsLoaded)
            _documents.Load(_repository.Load(project.RootPath));
        return GraphDocumentMapper.Load(_documents.LoadedProject, _groups);
    }

    public GraphNode ProjectNode(EditorGraphNodeDto node) => GraphDocumentMapper.CreateNode(node, _groups);

    public async Task<bool> SaveCurrentProjectAsync()
    {
        if (_projects.Current is not { } project)
            return false;

        await _projects.SaveAsync();
        return true;
    }

}
