using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Avalonia.GameView.ViewModels;
// Transitional adapter for legacy layouts; P5 removes it.
public sealed class AudioGalleryPageViewModel : BackNavigablePageViewModel, IGalleryPageViewModel, IDisposable
{
    public GalleryAudioState State { get; }
    public AudioGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
        State.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
    public System.Collections.ObjectModel.ObservableCollection<GalleryEntryViewModel> Items => State.Items;
    public string Title => State.Title;
    public string? ErrorMessage => State.ErrorMessage;
    public CommunityToolkit.Mvvm.Input.IRelayCommand<GalleryEntryViewModel> ToggleAudioCommand => State.ToggleAudioCommand;
    public Task ActivateAsync(GalleryTypeData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() { State.Stop(); base.OnBack(); }
    public void Dispose() => State.Dispose();
}
