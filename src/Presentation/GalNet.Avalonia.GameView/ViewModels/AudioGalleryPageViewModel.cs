using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class AudioGalleryPageViewModel(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IGalleryPageViewModel, IDisposable
{
    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    [ObservableProperty] private string _title = "Gallery";

    public Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        media.PlaybackChanged -= OnPlaybackChanged;
        media.PlaybackChanged += OnPlaybackChanged;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        Title = gallery.Type.TypeId;
        foreach (var item in gallery.Items.OrderBy(item => item.Item.Id))
            Items.Add(new GalleryEntryViewModel(item));
        UpdateAudioState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleAudio(GalleryEntryViewModel item)
    {
        if (!item.IsUnlocked) return;
        media.ToggleAudio(item.Data);
        UpdateAudioState();
    }

    [RelayCommand] private void Back() => navigation.GoBack();

    public void Dispose()
    {
        media.PlaybackChanged -= OnPlaybackChanged;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
    }

    private void OnPlaybackChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateAudioState();
        else Dispatcher.UIThread.Post(UpdateAudioState);
    }

    private void UpdateAudioState()
    {
        var duration = media.AudioDuration;
        var position = media.AudioPosition;
        foreach (var item in Items)
        {
            var active = item.Id == media.ActiveAudioItemId;
            item.IsPlaying = active && media.IsAudioPlaying;
            item.Progress = active && duration > 0 ? Math.Clamp(position * 100d / duration, 0, 100) : 0;
            item.TimeText = active ? $"{FormatTime(position)} / {FormatTime(duration)}" : "00:00 / 00:00";
        }
    }

    private static string FormatTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
