using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Avalonia.GameView.ViewModels;
// Transitional adapter for legacy layouts; P5 removes it.
public sealed class VideoGalleryPlayerPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    public GalleryVideoState State { get; }
    public VideoGalleryPlayerPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
        State.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
    public string Title { get => State.Title; set => State.Title = value; }
    public string? ErrorMessage { get => State.ErrorMessage; set => State.ErrorMessage = value; }
    public bool HasVideo { get => State.HasVideo; set => State.HasVideo = value; }
    public bool IsPlaying { get => State.IsPlaying; set => State.IsPlaying = value; }
    public bool CanSeek { get => State.CanSeek; set => State.CanSeek = value; }
    public double Progress { get => State.Progress; set => State.Progress = value; }
    public string TimeText { get => State.TimeText; set => State.TimeText = value; }
    public LibVLCSharp.Shared.MediaPlayer? VideoPlayer => State.VideoPlayer;
    public IReadOnlyList<GalNet.Rendering.Scene.SceneLayerItem> VideoLayers => State.VideoLayers;
    public CommunityToolkit.Mvvm.Input.IRelayCommand TogglePlaybackCommand => State.TogglePlaybackCommand;
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() { State.Stop(); base.OnBack(); }
    public void BeginSeek() => State.BeginSeek();
    public void EndSeek() => State.EndSeek();
    public void Dispose() => State.Dispose();
}
