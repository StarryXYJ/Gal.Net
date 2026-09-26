using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed partial class GalleryTypeAnnotationViewModel : ObservableObject
{
    private readonly Action<GalleryTypeAnnotationViewModel> _changed;
    private bool _ready;

    public GalleryTypeAnnotationViewModel(
        string typeId,
        bool isIncluded,
        string title,
        int? sortOrder,
        Action<GalleryTypeAnnotationViewModel> changed)
    {
        TypeId = typeId;
        _isIncluded = isIncluded;
        _title = title;
        _sortOrder = sortOrder;
        _changed = changed;
        _ready = true;
    }

    public string TypeId { get; }

    [ObservableProperty] private bool _isIncluded;
    [ObservableProperty] private string _title;
    [ObservableProperty] private int? _sortOrder;

    partial void OnIsIncludedChanged(bool value) => NotifyChanged();
    partial void OnTitleChanged(string value) => NotifyChanged();
    partial void OnSortOrderChanged(int? value) => NotifyChanged();

    private void NotifyChanged()
    {
        if (_ready) _changed(this);
    }
}
