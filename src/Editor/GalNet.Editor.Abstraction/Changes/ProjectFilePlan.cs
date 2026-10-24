namespace GalNet.Editor.Abstraction.Changes;

/// <summary>Disk publication attached to the same author transaction; copy sources are retained files, never byte buffers.</summary>
public sealed class ProjectFilePlan
{
    public Dictionary<string, byte[]> Writes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Copies { get; } = new(StringComparer.Ordinal);
    public List<string> Deletes { get; } = [];
    public List<string> CreateDirectories { get; } = [];
    public List<string> DeleteDirectories { get; } = [];
    public List<ProjectCaseRename> CaseRenames { get; } = [];
    public Dictionary<string, string?> ExpectedHashes { get; } = new(StringComparer.Ordinal);
}

public sealed record ProjectCaseRename(string Source, string Target, string? Temporary = null);

public interface IProjectFileStructureChange : IProjectStructureChange
{
    ProjectFilePlan Files { get; }
}
