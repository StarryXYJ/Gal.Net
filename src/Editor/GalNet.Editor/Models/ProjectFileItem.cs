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
    public string Icon => IsDirectory ? "📁" : Type == "sprite" ? "▧" : Type == "audio" ? "♫" : Type == "video" ? "▷" : "▤";
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isDropTarget;
    public void Dispose() => Thumbnail?.Dispose();
}

public sealed partial class ProjectDirectoryNode(string path, string name, Func<string, IEnumerable<ProjectDirectoryNode>> load, bool hasChildren = true) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = name;
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
