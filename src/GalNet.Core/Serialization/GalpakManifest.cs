namespace GalNet.Core.Serialization;

/// <summary>Verified contents of a distributable <c>.galpak</c> ZIP container.</summary>
public sealed record GalpakManifest(
    int Version,
    string ProjectId,
    string ProjectName,
    DateTimeOffset ExportedAt,
    IReadOnlyList<GalpakFileEntry> Files);

/// <summary>A file carried by a <see cref="GalpakManifest"/> and verified before installation.</summary>
public sealed record GalpakFileEntry(string Path, string Sha256, long Size);
