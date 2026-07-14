using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Abstraction.Services;

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
    private IList<MenuData> _menuItems = [];

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
        // Keep the menu bound to the page-owned observable collection. Rebuilding a
        // page menu (for example after saving or deleting a layout) then updates the
        // visible Menu without a second page navigation.
        MenuItems = page is IMenuProvider provider ? provider.MenuItems : [];
    }
}
