namespace GalNet.Core.Assets;

/// <summary>Direct references from an effective metadata snapshot, never a recursive loader.</summary>
public interface IAssetDependencies
{
    AssetDependencyInfo GetDependencies();
}

public sealed record AssetDependency(string AssetId, string? ExpectedTypeId = null, string? RequiredStoryPackage = null);

public sealed record AssetDependencyInfo(IReadOnlyList<AssetDependency> Direct, bool Complete);

/// <summary>Metadata for payloads whose compiler supplies their direct references.</summary>
public class DependentAssetMeta : AssetMeta, IAssetDependencies
{
    public List<AssetDependency> Dependencies { get; set; } = [];
    public bool DependenciesComplete { get; set; }

    public AssetDependencyInfo GetDependencies() => new(
        (Dependencies ?? throw new InvalidDataException("Dependency metadata cannot contain a null list.")).ToArray(), DependenciesComplete);
}
