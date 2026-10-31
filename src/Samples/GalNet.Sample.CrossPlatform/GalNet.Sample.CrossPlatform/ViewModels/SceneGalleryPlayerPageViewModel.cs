using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class SceneGalleryPlayerPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable, IAsyncDisposable
{
    public GalleryScenePlayback State { get; }
    public SceneGalleryPlayerPageViewModel(IGameNavigationService navigation, GalleryScenePlayback playback) : base(navigation)
    {
        State = playback;
        State.CloseRequested += OnBack;
    }
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() => _ = CloseAsync();
    private async Task CloseAsync() { if (await State.CloseAsync()) Navigation.GoBack(); }
    public ValueTask DisposeAsync() { State.CloseRequested -= OnBack; return State.DisposeAsync(); }
    public void Dispose() { State.CloseRequested -= OnBack; State.Dispose(); }
}
