using GalNet.Core.Configuration;

namespace GalNet.Editor.Abstraction.Documents;

/// <summary>A detached, validated disk candidate. Reading it never mutates the live project.</summary>
public sealed record ExternalProjectSnapshot(
    LoadedEditorProjectDocument Document,
    ProjectConfiguration Configuration,
    IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyDictionary<string, byte[]?> AuthorSources,
    string ContractsFingerprint);

public enum ExternalReloadResult { Unchanged, Reloaded, LocalChanges, Retry, Unavailable }
