using System.Collections.Generic;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorSaveCoordinator
{
    void SaveProjectDocument(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);

    string BuildPreviewData(
        string previewPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries);
}
