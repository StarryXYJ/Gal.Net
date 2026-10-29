using System.Collections.Generic;
using System.Threading;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorDocumentRepository
{
    LoadedEditorProjectDocument Load(string projectPath);

    IReadOnlyDictionary<string, byte[]?> SourceBaseline => new Dictionary<string, byte[]?>();
    void AdoptExternalBaseline(IReadOnlyDictionary<string, byte[]?> sources) => throw new NotSupportedException("External reload is unavailable for this repository.");

    ProjectSavePlan PrepareSave(string projectPath, EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);

    void ApplySave(string projectPath, ProjectSavePlan plan, CancellationToken cancellationToken = default);

    void Save(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);
}

public sealed record ProjectSavePlan(IReadOnlyDictionary<string, byte[]> Writes, IReadOnlyList<string> DeletedFiles)
{
    public GalNet.Editor.Abstraction.Changes.ProjectFilePlan? Files { get; init; }
}
