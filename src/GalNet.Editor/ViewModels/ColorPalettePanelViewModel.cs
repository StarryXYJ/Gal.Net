using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Shared.UI;
using GalNet.Editor.Abstraction.Services;
using GalNet.Core.Services;
using Avalonia.Media;

namespace GalNet.Editor.ViewModels;

public sealed partial class ColorPalettePanelViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    [ObservableProperty] private ColorItem? _selected;
    [ObservableProperty] private string _newKey = "new-color";
    [ObservableProperty] private string _message = "";
    public ObservableCollection<ColorItem> Colors { get; } = [];

    public ColorPalettePanelViewModel(IProjectService projects) { _projects = projects; _projects.CurrentChanged += _ => Reload(); Reload(); }
    [RelayCommand] private async Task AddAsync()
    {
        var provider = Provider();
        if (provider is null) return;
        if (string.IsNullOrWhiteSpace(NewKey) || !System.Text.RegularExpressions.Regex.IsMatch(NewKey, "^[a-zA-Z][a-zA-Z0-9_-]*$") || provider.Current.Colors.ContainsKey(NewKey))
        { Message = "Colour key must be unique and use letters, digits, '-' or '_'."; return; }
        Palette(provider).Set(NewKey, "#FFFFFFFF"); await SaveAsync(provider); Reload();
    }
    [RelayCommand] private async Task DeleteAsync(ColorItem? item)
    {
        var provider = Provider(); if (provider is null || item is null) return;
        var references = provider.Current.Widgets.Values.Sum(c => c.ColorOverrides.Values.Count(v => string.Equals(v, item.Key, StringComparison.OrdinalIgnoreCase)));
        if (references > 0) { Message = $"'{item.Key}' is used by {references} control setting(s)."; return; }
        provider.Current.Colors.Remove(item.Key); provider.NotifyChanged(); await SaveAsync(provider); Reload();
    }
    partial void OnSelectedChanged(ColorItem? value) { }
    private async Task UpdateAsync(ColorItem item)
    {
        var provider = Provider(); if (provider is null || !item.Value.StartsWith('#')) return;
        Palette(provider).Set(item.Key, item.Value); await SaveAsync(provider);
    }
    private static Task SaveAsync(IUiProjectProvider provider) => provider.SaveAsync();
    private IUiProjectProvider? Provider() => _projects.Current?.UiProject;
    private ProjectColorPalette Palette(IUiProjectProvider provider) => (ProjectColorPalette)_projects.Current!.Palette;
    private void Reload()
    {
        Colors.Clear();
        var provider = Provider(); if (provider is null) return;
        foreach (var color in provider.Current.Colors.OrderBy(x => x.Key)) Colors.Add(new ColorItem(color.Key, color.Value, UpdateAsync));
    }
}

public sealed partial class ColorItem : ObservableObject
{
    private readonly Func<ColorItem, Task>? _changed;
    public string Key { get; }
    [ObservableProperty] private string _value;
    [ObservableProperty] private Color _color;

    public ColorItem(string key, string value, Func<ColorItem, Task>? changed = null)
    {
        Key = key; _value = value;
        _changed = changed;
        _color = Color.TryParse(value, out var color) ? color : Colors.White;

    }
    partial void OnColorChanged(Color value)
    {
        Value = value.ToString();
    }
    partial void OnValueChanged(string value)
    {
        if (Color.TryParse(value, out var color) && color != Color) Color = color;
        if (_changed is not null) _ = _changed(this);
    }
}
