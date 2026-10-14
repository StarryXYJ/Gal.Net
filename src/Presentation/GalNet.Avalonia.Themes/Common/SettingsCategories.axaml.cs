using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using GalNet.Game.Controls.Forms;
using Avalonia.Markup.Xaml;
using GalNet.Avalonia.GameView.Settings;

namespace GalNet.Avalonia.Themes.Common;

public sealed partial class SettingsCategories : UserControl
{
    private SettingItem[] _observedItems = [];
    private bool _attached;
    public ObservableCollection<SettingCategory> VisibleCategories { get; } = [];
    public static readonly StyledProperty<EditorRegistry?> EditorsProperty =
        AvaloniaProperty.Register<SettingsCategories, EditorRegistry?>(nameof(Editors));
    public EditorRegistry? Editors
    {
        get => GetValue(EditorsProperty);
        set => SetValue(EditorsProperty, value);
    }
    public static readonly StyledProperty<IReadOnlyList<SettingCategory>?> CategoriesProperty =
        AvaloniaProperty.Register<SettingsCategories, IReadOnlyList<SettingCategory>?>(nameof(Categories));
    public IReadOnlyList<SettingCategory>? Categories
    {
        get => GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }
    public SettingsCategories() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != CategoriesProperty) return;
        ObserveItems();
        RefreshCategories();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        ObserveItems();
        RefreshCategories();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ObserveItems();
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveItems()
    {
        foreach (var item in _observedItems) item.PropertyChanged -= OnItemChanged;
        _observedItems = _attached ? Categories?.SelectMany(category => category.Items).ToArray() ?? [] : [];
        foreach (var item in _observedItems) item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingItem.IsVisible)) RefreshCategories();
    }

    private void RefreshCategories()
    {
        var visible = Categories?.Where(category => category.Items.Any(item => item.IsVisible)).ToArray() ?? [];
        if (VisibleCategories.SequenceEqual(visible)) return;
        var tabs = this.FindControl<TabControl>("CategoryTabs");
        var selected = tabs?.SelectedItem as SettingCategory;
        VisibleCategories.Clear();
        foreach (var category in visible) VisibleCategories.Add(category);
        if (tabs is not null) tabs.SelectedItem = selected is not null && visible.Contains(selected) ? selected : visible.FirstOrDefault();
    }
}
