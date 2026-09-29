using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Explicit diagnostic page for Gallery content with no registered frontend page.</summary>
public sealed partial class MissingGalleryPageViewModel(IGameNavigationService navigation)
    : PageViewModelBase, IGalleryPageViewModel
{
    [ObservableProperty] private string _message = "No Gallery page is registered.";

    public Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Message = $"No Gallery page is registered for type '{gallery.Type.TypeId}'.";
        return Task.CompletedTask;
    }

    [RelayCommand] private void Back() => navigation.GoBack();
}
