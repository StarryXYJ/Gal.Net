namespace GalNet.Editor.Abstraction.Commands;

public sealed record ProjectCreateRequest(string Path, string? Name = null, string? ContractsDirectory = null, bool DryRun = false);
public sealed record ProjectCreateReport(string ProjectPath, string Name, bool DryRun, bool ContractReady,
    string? ContractSha256, IReadOnlyList<string> Files);
