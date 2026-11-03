using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class AudioGalleryPageViewModel : BackNavigablePageViewModel, IGalleryPageViewModel, IDisposable
{
    public GalleryAudioState State { get; }
    public PageSlice<GalNet.Avalonia.GameView.ViewModels.GalleryEntryViewModel> Pages { get; }
    public AudioGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
        Pages = new(State.Items, SampleCustomization.PageSize);
    }
    public Task ActivateAsync(GalleryTypeData item, CancellationToken cancellationToken = default)
    {
        Task? activation = null;
        Pages.UpdateSource(() => activation = State.ActivateAsync(item, cancellationToken));
        return activation!;
    }
    protected override void OnBack() { State.Stop(); base.OnBack(); }
    public void Dispose() { Pages.Dispose(); State.Dispose(); }
}
