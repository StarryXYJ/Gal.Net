using System.Collections.Generic;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorDocumentRepository
{
    LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings);

    void Save(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);
}
