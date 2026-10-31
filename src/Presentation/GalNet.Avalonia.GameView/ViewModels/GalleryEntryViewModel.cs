using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryEntryViewModel(GalleryItemData data, string? name = null, Action<double>? seek = null, string unnamedTitle = "Untitled") : ObservableObject, IDisposable
{
    public GalleryItemData Data { get; } = data;
    public int Id => Data.Item.Id;
    public string Name { get; } = data.IsUnlocked ? name ?? "" : "";
    public bool HasName => Name.Length > 0;
    public string Title => IsLocked ? "???" : HasName ? Name : $"{unnamedTitle} {Id.ToString(CultureInfo.InvariantCulture)}";
    private bool _updating;
    private bool _seeking;
    public void BeginSeek() => _seeking = CanSeek;
    public void EndSeek() { if (!_seeking) return; _seeking = false; if (CanSeek) seek?.Invoke(Progress); }
    [ObservableProperty] private bool _canSeek;
    partial void OnProgressChanged(double value) { if (!_updating && !_seeking && CanSeek) seek?.Invoke(value); }
    public void UpdateProgress(double value) { if (_seeking && CanSeek) return; _seeking = false; _updating = true; try { Progress = value; } finally { _updating = false; } }
    public bool IsUnlocked => Data.IsUnlocked;
    public bool IsLocked => !IsUnlocked;

    public bool HasThumbnail => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _timeText = "00:00 / 00:00";

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
