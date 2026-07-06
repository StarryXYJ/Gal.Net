using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IEditorLocalizationService _localization;

    public INavigationService Navigation { get; }

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _windowTitle = "";

    [ObservableProperty]
    private AvaloniaList<MenuData> _menuItems = new();

    public MainWindowViewModel(INavigationService navigation, IEditorLocalizationService localization)
    {
        Navigation = navigation;
        _localization = localization;
        WindowTitle = _localization["App.Title"];

        Navigation.CurrentPageChanged += page =>
        {
            CurrentPage = page;
            WindowTitle = page is PageViewModelBase pvm ? pvm.Title : _localization["App.Title"];
            UpdateMenuItems(page);
        };

        _localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                WindowTitle = CurrentPage is PageViewModelBase pvm ? pvm.Title : _localization["App.Title"];
        };
    }

    private void UpdateMenuItems(object? page)
    {
        MenuItems.Clear();
        if (page is IMenuProvider provider && provider.MenuItems is { } items)
        {
            foreach (var item in items)
                MenuItems.Add(item);
        }
    }
}
