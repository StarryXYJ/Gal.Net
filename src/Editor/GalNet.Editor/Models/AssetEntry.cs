using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Editor.Models;

public sealed partial class AssetEntry : ObservableObject
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
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isDropTarget;
    public bool HasThumbnail => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(IsOtherFile));
    }
    public bool IsImage => Type == "sprite";
    public bool IsAudio => Type == "audio";
    public bool IsVideo => Type == "video";
    public bool IsOtherFile => !IsDirectory && !IsAudio && !IsVideo && !HasThumbnail;
    public string MetaPath => IsDirectory ? string.Empty : FullPath + ".meta";
}
