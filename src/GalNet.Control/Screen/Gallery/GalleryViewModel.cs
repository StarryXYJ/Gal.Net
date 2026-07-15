using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Core.Gallery;
using GalNet.Core.Services;
using GalNet.Core.Widget;

namespace GalNet.Control.ViewModels;

public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator;
    private readonly IGameProgressService? _progress;
    private readonly GameFlowOptions _options;
    private Func<WidgetHostViewModel>? _categoryHostFactory;
    private Func<WidgetHostViewModel>? _itemHostFactory;
    public ObservableCollection<WidgetHostViewModel> CategoryHosts { get; } = [];
    public ObservableCollection<WidgetHostViewModel> ItemHosts { get; } = [];
    public WidgetHostViewModel BackButtonHost { get; private set; } = null!;
    [ObservableProperty] private bool _showCategorySelector;
    [ObservableProperty] private GalleryCategory _selectedCategory;

    public GalleryViewModel(IGameScreenNavigator navigator, GameFlowOptions options, IGameProgressService? progress)
    { _navigator = navigator; _options = options; _progress = progress; }

    public void SetHosts(Func<WidgetHostViewModel> categoryHostFactory, Func<WidgetHostViewModel> itemHostFactory, WidgetHostViewModel back)
    {
        _categoryHostFactory = categoryHostFactory; _itemHostFactory = itemHostFactory; BackButtonHost = back;
        var button = back.RequireWidget<IButtonWidget>(); button.Text = "Back"; button.Command = BackCommand;
        BuildCategories();
    }

    private void BuildCategories()
    {
        CategoryHosts.Clear();
        var categories = _options.GalleryConfiguration?.Items.Select(item => item.Category).Distinct().ToArray() ?? [];
        ShowCategorySelector = categories.Length > 1;
        SelectedCategory = categories.FirstOrDefault();
        if (_categoryHostFactory is not null)
            foreach (var category in categories)
            {
                var host = _categoryHostFactory(); var button = host.RequireWidget<IButtonWidget>(); button.Text = category.ToString();
                button.Command = new RelayCommand(() => SelectCategory(category)); CategoryHosts.Add(host);
            }
        RefreshItems();
    }

    private void SelectCategory(GalleryCategory category) { SelectedCategory = category; RefreshItems(); }

    private void RefreshItems()
    {
        ItemHosts.Clear();
        if (_itemHostFactory is null) return;
        foreach (var item in _options.GalleryConfiguration?.Items.Where(item => item.Category == SelectedCategory && (_progress?.IsGalleryUnlocked(item.Category, item.SequenceId) ?? false)) ?? [])
        {
            var host = _itemHostFactory(); var button = host.RequireWidget<IButtonWidget>();
            button.Text = item.Title ?? $"{item.Category} {item.SequenceId + 1}"; button.Command = new RelayCommand(() => Open(item)); ItemHosts.Add(host);
        }
    }

    private void Open(GalleryItem item) { if (item.Category == GalleryCategory.Scene) _ = _navigator.NavigateAsync("game", item.ResourceId); }
    [RelayCommand] private Task BackAsync() => _navigator.GoBackAsync();
}
