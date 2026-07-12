using System;

namespace GalNet.Editor.Models;

public sealed class AssetEntry
{
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public string Type { get; init; } = "unknown";
    public string? Id { get; init; }
    public string? Filter { get; init; }
    public string? Compress { get; init; }
    public bool HasValidMeta { get; init; }
    public bool IsImage => Type == "sprite";
    public bool IsAudio => Type == "audio";
    public bool IsVideo => Type == "video";
    public string MetaPath => IsDirectory ? string.Empty : FullPath + ".meta";
    public string IconText => IsDirectory
        ? Name.Equals("Layer", StringComparison.OrdinalIgnoreCase) ? "▧" : Name.Equals("Audio", StringComparison.OrdinalIgnoreCase) ? "♫" : Name.Equals("Video", StringComparison.OrdinalIgnoreCase) ? "▶" : "▰"
        : IsAudio ? "♫" : IsVideo ? "▶" : IsImage ? "▣" : "□";
}
