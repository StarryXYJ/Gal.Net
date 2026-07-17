using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.UI;

namespace GalNet.Editor.Shared.Commands;

public sealed class EditorSessionFactory
{
    private readonly IEditorDocumentRepository _repository;
    private readonly IReadOnlyList<GalNet.Editor.Abstraction.Commands.IEditorCommandHandler> _handlers;

    public EditorSessionFactory(
        IEditorDocumentRepository? repository = null,
        IEnumerable<GalNet.Editor.Abstraction.Commands.IEditorCommandHandler>? handlers = null)
    {
        _repository = repository ?? new EditorDocumentRepository();
        _handlers = (handlers ?? [new BuiltInEditorCommandHandler()]).ToList();
    }

    public EditorSession Open(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        var settings = FileEditorSessionPersistence.LoadSettings(projectPath);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath));
        var loaded = _repository.Load(projectPath, name, settings);
        var document = new EditorProjectDocument
        {
            Graph = loaded.Document,
            GroupEntries = loaded.GroupEntries,
            Settings = settings,
            UiProject = new FileUiProjectProvider(projectPath).Current
        };
        return new EditorSession(
            document,
            new FileEditorSessionPersistence(projectPath, _repository),
            _handlers);
    }
}
