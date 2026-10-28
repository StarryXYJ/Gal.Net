using GalNet.Core.Assets;

namespace GalNet.Core.Serialization;

/// <summary>Bootstrap descriptor for the project's one addressable graph.</summary>
public sealed record GameContentManifest
{
    public const string FileName = "content.json";
    public int Version { get; init; } = 1;
    public string GraphId { get; init; } = StoryResourceIds.Graph;
}
