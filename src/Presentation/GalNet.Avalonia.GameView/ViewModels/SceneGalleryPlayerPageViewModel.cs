using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Avalonia.GameView.Page;
namespace GalNet.Avalonia.GameView.ViewModels;
// Transitional adapter for legacy layouts; P5 removes it.
public sealed class SceneGalleryPlayerPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IAsyncDisposable, IDisposable
{
    public GalleryScenePlayback State { get; }
    public GamePresentationState Gameplay => State.Gameplay;
    public GamePage Surface => State.Surface;
    public string? ErrorMessage => State.ErrorMessage;
    public SceneGalleryPlayerPageViewModel(IGameNavigationService navigation, IGameSessionService session) : base(navigation)
    {
        State = new(session);
        State.CloseRequested += OnBack;
        State.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() => _ = CloseAsync();
    private async Task CloseAsync() { if (await State.CloseAsync()) Navigation.GoBack(); }
    public ValueTask DisposeAsync() { State.CloseRequested -= OnBack; return State.DisposeAsync(); }
    public void Dispose() { State.CloseRequested -= OnBack; State.Dispose(); }
}
