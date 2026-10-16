using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Changes;

/// <summary>A deterministic, atomic transformation of structural identities/references only.
/// It is applied independently to working content and the current saved baseline, never to an old whole-project snapshot.</summary>
public interface IProjectStructureChange
{
    void Apply(EditorProjectDocument document, AuthorDocumentVersion version);
}

public enum AuthorDocumentVersion { Working, Saved }
