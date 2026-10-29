using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Editor.Models;

public sealed partial class ProjectFileItem : ObservableObject, IDisposable
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public string Type { get; init; } = "file";
    public DateTime WriteTimeUtc { get; init; }
    public string ProtectionReason { get; init; } = "";
    public bool CanEdit => ProtectionReason.Length == 0;
    public Avalonia.Media.Geometry IconGeometry => Avalonia.Media.Geometry.Parse("F0 " + (IsDirectory ? DirectoryIcon(Name) : Type switch
    {
        "sprite" => "M3 3H21V21H3ZM5 17L9 12L12 15L15 11L19 17Z",
        "audio" => "M10 4L20 2V15A3 3 0 1 1 18 12.2V6L12 7V17A3 3 0 1 1 10 14.2Z",
        "video" => "M3 5H21V19H3ZM10 8V16L16 12Z",
        _ when Name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) => "M12 2L14 4L17 4L18 7L21 9L20 12L21 15L18 17L17 20L14 20L12 22L10 20L7 20L6 17L3 15L4 12L3 9L6 7L7 4L10 4ZM12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8Z",
        _ => "M4 3H15L20 8V21H4ZM14 4V9H19ZM7 12H17V14H7ZM7 16H17V18H7Z"
    }));
    internal static string DirectoryIcon(string name) => name.ToLowerInvariant() switch
    {
        "assets" => "M2 5H10L12 7H22V19H2ZM7 16L10 12L12 14L15 11L19 16Z",
        "contracts" => "M2 5H10L12 7H22V19H2ZM8 10H16V12H8ZM8 14H16V16H8Z",
        "i18n" => "M2 5H10L12 7H22V19H2ZM8 10H16V12H8ZM11 9H13V17H11Z",
        _ => "M2 5H10L12 7H22V19H2Z"
    };
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isDropTarget;
    public void Dispose() => Thumbnail?.Dispose();
}

public sealed partial class ProjectDirectoryNode(string path, string name, Func<string, IEnumerable<ProjectDirectoryNode>> load, bool hasChildren = true) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public Avalonia.Media.Geometry IconGeometry => Avalonia.Media.Geometry.Parse("F0 " + ProjectFileItem.DirectoryIcon(Name));
    public ObservableCollection<ProjectDirectoryNode> Children { get; } = hasChildren ? [new(path, "…", _ => [], false)] : [];
    private bool _loaded = !hasChildren;
    [ObservableProperty] private bool _isExpanded;
    partial void OnIsExpandedChanged(bool value) { if (value) EnsureChildren(); }
    public void EnsureChildren()
    {
        if (_loaded) return; _loaded = true;
        Children.Clear();
        foreach (var child in load(Path)) Children.Add(child);
    }
}
