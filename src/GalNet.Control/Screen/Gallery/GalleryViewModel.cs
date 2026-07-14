using System.Collections.ObjectModel;
using GalNet.Core.Gallery;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

/// <summary>Runtime gallery. Entries remain hidden until the global progress store unlocks them.</summary>
public sealed class GalleryViewModel
{
    private readonly INavigationService _navigation;
    private readonly IGameProgressService? _progress;
    private readonly GameFlowOptions? _options;
    private readonly IGameFlowFactory _factory;
    public ObservableCollection<GalleryItem> Items { get; } = [];
    public IReadOnlyList<GalleryCategory> Categories { get; }
    public bool ShowCategorySelector => Categories.Count > 1;
    public GalleryCategory SelectedCategory { get; private set; }

    public GalleryViewModel(INavigationService navigation, IGameFlowFactory factory, GameFlowOptions? options, IGameProgressService? progress)
    {
        _navigation = navigation; _factory = factory; _options = options; _progress = progress;
        Categories = options?.GalleryConfiguration?.Items.Select(x => x.Category).Distinct().ToArray() ?? [];
        SelectedCategory = Categories.FirstOrDefault(); Refresh();
    }
    public void SelectCategory(GalleryCategory category) { SelectedCategory = category; Refresh(); }
    public void Refresh()
    {
        Items.Clear();
        foreach (var item in _options?.GalleryConfiguration?.Items.Where(x => x.Category == SelectedCategory && (_progress?.IsGalleryUnlocked(x.Category, x.SequenceId) ?? false)) ?? []) Items.Add(item);
    }
    public void Open(GalleryItem item)
    {
        // Scene items reuse their configured group directly. Image/video presentation graphs are
        // intentionally represented by the same start-node contract once the asset graph builder is supplied.
        if (item.Category == GalleryCategory.Scene)
            _navigation.NavigateTo(_factory.CreateRun(_navigation, _options with { StartNodeId = item.ResourceId, IsGalleryPresentation = true }));
    }
    public void Back() => _navigation.GoBack();
}
