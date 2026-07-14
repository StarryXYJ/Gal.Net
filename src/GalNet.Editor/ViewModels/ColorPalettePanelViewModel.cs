using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.UI;
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
        provider.Current.Colors[NewKey] = "#FFFFFFFF"; await SaveAsync(provider); Reload();
    }
    [RelayCommand] private async Task DeleteAsync(ColorItem? item)
    {
        var provider = Provider(); if (provider is null || item is null) return;
        var references = provider.Current.Widgets.Values.Sum(c => c.ColorOverrides.Values.Count(v => string.Equals(v, item.Key, StringComparison.OrdinalIgnoreCase)));
        if (references > 0) { Message = $"'{item.Key}' is used by {references} control setting(s)."; return; }
        provider.Current.Colors.Remove(item.Key); await SaveAsync(provider); Reload();
    }
    partial void OnSelectedChanged(ColorItem? value) { if (value is not null) _ = UpdateAsync(value); }
    private async Task UpdateAsync(ColorItem item)
    {
        var provider = Provider(); if (provider is null || !item.Value.StartsWith('#')) return;
        provider.Current.Colors[item.Key] = item.Value; await SaveAsync(provider);
    }
    private async Task SaveAsync(IUiProjectProvider provider) { provider.NotifyChanged(); await provider.SaveAsync(); }
    private IUiProjectProvider? Provider() => _projects.Current?.UiProject;
    private void Reload()
    {
        Colors.Clear();
        var provider = Provider(); if (provider is null) return;
        foreach (var color in provider.Current.Colors.OrderBy(x => x.Key)) Colors.Add(new ColorItem(color.Key, color.Value));
    }
}

public sealed partial class ColorItem : ObservableObject
{
    public string Key { get; }
    [ObservableProperty] private string _value;
    [ObservableProperty] private Color _color;

    public ColorItem(string key, string value)
    {
        Key = key; _value = value;
        _color = Color.TryParse(value, out var color) ? color : Colors.White;

    }
    partial void OnColorChanged(Color value)
    {
        Value = value.ToString();
    }
    partial void OnValueChanged(string value)
    {
        if (Color.TryParse(value, out var color) && color != Color) Color = color;
    }
}
