using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryEntryViewModel(GalleryItemData data) : ObservableObject, IDisposable
{
    public GalleryItemData Data { get; } = data;
    public string Id => Data.Item.Id;
    public string Title => string.IsNullOrWhiteSpace(Data.Item.Title) ? Data.Item.Id : Data.Item.Title;
    public bool IsUnlocked => Data.IsUnlocked;
    public bool IsLocked => !IsUnlocked;

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
