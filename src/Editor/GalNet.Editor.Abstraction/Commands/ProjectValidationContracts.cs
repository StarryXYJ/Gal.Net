namespace GalNet.Editor.Abstraction.Commands;

public sealed record ProjectValidationReport(string Scope, bool StructureOnly, string SemanticStatus,
    IReadOnlyList<string> CheckedFiles, IReadOnlyList<string> Checks, IReadOnlyList<string> SkippedChecks,
    bool Complete, string? ContractSha256 = null);
