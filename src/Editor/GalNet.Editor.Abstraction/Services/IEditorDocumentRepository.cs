using System.Collections.Generic;
using System.Threading;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorDocumentRepository
{
    LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings);

    ProjectSavePlan PrepareSave(string projectPath, EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);

    void ApplySave(string projectPath, ProjectSavePlan plan, CancellationToken cancellationToken = default);

    void Save(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);
}

public sealed record ProjectSavePlan(IReadOnlyDictionary<string, byte[]> Writes, IReadOnlyList<string> DeletedFiles);
