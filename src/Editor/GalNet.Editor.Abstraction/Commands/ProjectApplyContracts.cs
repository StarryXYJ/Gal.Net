namespace GalNet.Editor.Abstraction.Commands;

/// <summary>Native author files and resources, published together after validating the final project.</summary>
public sealed class ProjectApplyManifest
{
    public required int Version { get; init; }
    public required IReadOnlyList<ProjectApplyWrite> Writes { get; init; }
    public required IReadOnlyList<ProjectApplyDelete> Deletes { get; init; }
}

public sealed class ProjectApplyWrite
{
    public required string Path { get; init; }
    public required string Source { get; init; }
    public required string ContentHash { get; init; }
    /// <summary>Null explicitly requires a new target; otherwise SHA-256 of the current target.</summary>
    public required string? ExpectedHash { get; init; }
}

public sealed class ProjectApplyDelete
{
    public required string Path { get; init; }
    public required string ExpectedHash { get; init; }
}

public sealed record ProjectApplyReport(bool DryRun, IReadOnlyList<string> WrittenFiles, IReadOnlyList<string> DeletedFiles,
    IReadOnlyDictionary<string, string> ContentHashes, ProjectValidationReport Validation, bool ReferencesComplete = false);
