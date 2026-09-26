using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryPageViewModel(IGameNavigationService navigation)
    : PageViewModelBase, IActivatablePageViewModel<IReadOnlyList<GalleryTypeData>>
{
    public ObservableCollection<GalleryTypeData> Types { get; } = [];

    [ObservableProperty] private bool _hasTypes;

    public Task ActivateAsync(IReadOnlyList<GalleryTypeData> args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Types.Clear();
        foreach (var type in args) Types.Add(type);
        HasTypes = Types.Count > 0;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenTypeAsync(GalleryTypeData type, CancellationToken cancellationToken) =>
        navigation.NavigateAsync<GalleryContentPageViewModel, GalleryTypeData>(type, cancellationToken);

    [RelayCommand] private void Back() => navigation.GoBack();
}
