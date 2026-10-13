namespace GalNet.Editor.Abstraction.Commands;

public sealed record ProjectEditReport(bool DryRun, IReadOnlyList<string> Descriptions,
    IReadOnlyList<string> ChangedResources, IReadOnlyList<string> WrittenFiles, IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> CreatedNodeIds, IReadOnlyList<ProjectReferenceItem> References, bool? ReferencesComplete);
