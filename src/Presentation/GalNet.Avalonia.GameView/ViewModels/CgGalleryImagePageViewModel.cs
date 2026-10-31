using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Avalonia.GameView.ViewModels;
// Transitional adapter for legacy layouts; P5 removes it.
public sealed class CgGalleryImagePageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    public GalleryImageState State { get; }
    public CgGalleryImagePageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
        State.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
    public string Title { get => State.Title; set => State.Title = value; }
    public string? ErrorMessage { get => State.ErrorMessage; set => State.ErrorMessage = value; }
    public global::Avalonia.Media.Imaging.Bitmap? Image { get => State.Image; set => State.Image = value; }
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    public void Dispose() => State.Dispose();
}
