using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Owns audio preparation, retained positions and playback notifications independently of page routing.</summary>
public sealed partial class GalleryAudioState(
    GalleryMediaService media) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _preparation;
    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _title = "Gallery";

    public async Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _preparation?.Cancel(); _preparation?.Dispose();
        _preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var preparation = _preparation;
        media.PlaybackChanged -= OnPlaybackChanged;
        media.PlaybackChanged += OnPlaybackChanged;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        Title = media.ResolveText(gallery.Type.DisplayNameKey, gallery.Type.TypeId);
        foreach (var item in gallery.Items.OrderBy(item => item.Item.Id))
            Items.Add(new GalleryEntryViewModel(item, media.ResolveName(item.Item), percent => media.SeekAudio(item, percent), media.ResolveText("Gallery.Untitled", "Untitled")));
        UpdateAudioState();
        try
        {
            foreach (var item in gallery.Items.Where(item => item.IsUnlocked))
                await media.PrepareAudioAsync(item, preparation.Token);
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested) { }
    }

    [RelayCommand]
    private void ToggleAudio(GalleryEntryViewModel item)
    {
        if (!item.IsUnlocked) return;
        media.ToggleAudio(item.Data);
        UpdateAudioState();
    }


    public void Stop() { _preparation?.Cancel(); media.StopPlayback(); }

    public void Dispose()
    {
        _preparation?.Cancel(); _preparation?.Dispose(); _preparation = null;
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
        ErrorMessage = media.LastError;
        foreach (var item in Items)
        {
            var active = item.Id == media.ActiveAudioItemId;
            item.IsPlaying = active && media.IsAudioPlaying;
            var preview = media.GetAudioPreview(item.Data);
            item.CanSeek = item.IsUnlocked;
            item.UpdateProgress(preview.Progress);
            item.TimeText = $"{FormatTime(preview.Position)} / {FormatTime(preview.Duration)}";
        }
    }

    private static string FormatTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
