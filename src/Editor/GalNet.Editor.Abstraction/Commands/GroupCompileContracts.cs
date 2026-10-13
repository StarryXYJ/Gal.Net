namespace GalNet.Editor.Abstraction.Commands;

public sealed record GroupCompileRequest(GroupSelector Selector, string? Output = null, bool DryRun = false);
public sealed record GroupCompileReport(string Source, string Output, bool DryRun, int SourceEntryCount,
    int CompiledEntryCount, IReadOnlyDictionary<string, IReadOnlyList<string>> SourceMap, string ContractSha256);
