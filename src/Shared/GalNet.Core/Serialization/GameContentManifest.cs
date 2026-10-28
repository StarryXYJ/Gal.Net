using GalNet.Core.Assets;

namespace GalNet.Core.Serialization;

/// <summary>Bootstrap descriptor for the project's one addressable graph.</summary>
public sealed record GameContentManifest
{
    public const string FileName = "content.json";
    public int Version { get; init; } = 2;
    public string GraphId { get; init; } = StoryResourceIds.Graph;
    public string ProjectId { get; init; } = "";
    public string BuildId { get; init; } = "";
    public List<GalpakFileEntry> BasePaks { get; init; } = [];
    public List<StoryPackageManifest> Stories { get; init; } = [];
}

public sealed record StoryPackageManifest(string PackageId, List<GalpakFileEntry> Parts);
