using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class AudioGalleryPageViewModel : BackNavigablePageViewModel, IGalleryPageViewModel, IDisposable
{
    public GalleryAudioState State { get; }
    public AudioGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
    }
    public Task ActivateAsync(GalleryTypeData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() { State.Stop(); base.OnBack(); }
    public void Dispose() => State.Dispose();
}
