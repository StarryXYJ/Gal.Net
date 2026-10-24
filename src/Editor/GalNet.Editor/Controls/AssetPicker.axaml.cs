using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Core.Assets;
using GalNet.Editor.Models;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-only ID-backed media picker. Game runtime UI never references this control.</summary>
public partial class AssetPicker : UserControl
{
    public static readonly StyledProperty<string> SelectedAssetIdProperty = AvaloniaProperty.Register<AssetPicker, string>(nameof(SelectedAssetId), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<AssetPickerFilter> FilterProperty = AvaloniaProperty.Register<AssetPicker, AssetPickerFilter>(nameof(Filter));
    public static readonly StyledProperty<IAssetManager?> AssetManagerProperty = AvaloniaProperty.Register<AssetPicker, IAssetManager?>(nameof(AssetManager));
    public static readonly DirectProperty<AssetPicker, string> DisplayTextProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayText), x => x.DisplayText);
    public static readonly DirectProperty<AssetPicker, string> DisplayIconProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayIcon), x => x.DisplayIcon);
    public static readonly DirectProperty<AssetPicker, string> DetailPathProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DetailPath), x => x.DetailPath);

    private string _displayText = "None", _displayIcon = "—", _detailPath = string.Empty;
    private IReadOnlyList<AssetPickerItem> _allItems = [];
    private AssetPickerItem? _current;
    private int _resolveVersion;
    private bool _flyoutOpen;

    public ObservableCollection<AssetPickerItem> Items { get; } = [];
    public string SelectedAssetId { get => GetValue(SelectedAssetIdProperty); set => SetValue(SelectedAssetIdProperty, value ?? string.Empty); }
    public AssetPickerFilter Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    public IAssetManager? AssetManager { get => GetValue(AssetManagerProperty); set => SetValue(AssetManagerProperty, value); }
    public string DisplayText => _displayText;
    public string DisplayIcon => _displayIcon;
    public string DetailPath => _detailPath;

    static AssetPicker()
    {
        SelectedAssetIdProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { _ = picker.ResolveCurrentAsync(); });
        AssetManagerProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { _ = picker.ResolveCurrentAsync(); });
        FilterProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { if (picker._flyoutOpen) _ = picker.LoadItemsAsync(); });
    }

    public AssetPicker()
    {
        InitializeComponent();
        PickerPanel.Search.PlaceholderText = "Search assets";
        PickerPanel.Confirmed += () =>
        {
            if (PickerPanel.Selected is not { } item) return;
            SelectedAssetId = item.Id; PickerButton.Flyout?.Hide();
        };
        PickerPanel.Cancelled += () => PickerButton.Flyout?.Hide();
    }
    public ResourcePickerPanel ResourcePanel => PickerPanel;

    private async void OnFlyoutOpened(object? sender, EventArgs e)
    {
        _flyoutOpen = true;
        await LoadItemsAsync();
        Dispatcher.UIThread.Post(() => PickerPanel.Search.Focus());
    }

    private void OnFlyoutClosed(object? sender, EventArgs e) => _flyoutOpen = false;

    private async Task ResolveCurrentAsync()
    {
        var version = Interlocked.Increment(ref _resolveVersion);
        var assetId = SelectedAssetId;
        var assetManager = AssetManager;
        AssetPickerItem? current = null;
        if (!string.IsNullOrWhiteSpace(assetId) && assetManager is not null)
        {
            var file = await assetManager.GetFileAsync(assetId);
            if (file is not null) current = await AssetPickerItem.CreateAsync(file);
        }
        if (version != _resolveVersion || !string.Equals(assetId, SelectedAssetId, StringComparison.Ordinal)) return;
        _current = current;
        NotifyDisplay(assetId);
    }

    private async Task LoadItemsAsync()
    {
        var assetManager = AssetManager;
        if (assetManager is null) { _allItems = []; ApplyFilter(null); return; }
        var type = Filter switch { AssetPickerFilter.Image => "sprite", AssetPickerFilter.Audio => "audio", AssetPickerFilter.Video => "video", AssetPickerFilter.EffectProgram => "effect-program", AssetPickerFilter.ImageVariants => "image-variants", _ => null };
        var files = await assetManager.GetFilesAsync(type);
        if (Filter == AssetPickerFilter.All) files = files.Where(x => x.TypeId is "sprite" or "audio" or "video" or "effect-program" or "image-variants").ToArray();
        else if (Filter == AssetPickerFilter.Text) files = files.Where(x => IsTextPath(x.Path)).ToArray();
        _allItems = await Task.WhenAll(files.Select(AssetPickerItem.CreateAsync));
        ApplyFilter(null);
    }

    internal static bool IsTextPath(string path) => AssetPickerTextFileExtensions.IsSupported(path);

    private void ApplyFilter(string? search)
    {
        Items.Clear(); Items.Add(AssetPickerItem.Empty);
        foreach (var item in _allItems) Items.Add(item);
        PickerPanel.SetItems(Items.Select(item => new ResourcePickerItem(item.File?.Id ?? "", item.Name, item.Path, item.Icon, item.Thumbnail)).ToArray());
    }

    private void NotifyDisplay(string assetId)
    {
        SetAndRaise(DisplayTextProperty, ref _displayText, string.IsNullOrWhiteSpace(assetId) ? "None" : _current?.Name ?? "Missing!");
        SetAndRaise(DisplayIconProperty, ref _displayIcon, string.IsNullOrWhiteSpace(assetId) ? "—" : _current is null ? "!" : _current.Icon);
        SetAndRaise(DetailPathProperty, ref _detailPath, _current?.Path ?? string.Empty);
    }
}

public sealed class AssetPickerItem
{
    public static AssetPickerItem Empty { get; } = new(null, "None", string.Empty, "×", null);
    private AssetPickerItem(IGameFile? file, string name, string path, string icon, Bitmap? thumbnail) { File = file; Name = name; Path = path; Icon = file is not null && AssetPicker.IsTextPath(path) ? "T" : icon; Thumbnail = thumbnail; }
    public IGameFile? File { get; }
    public string Name { get; }
    public string Path { get; }
    public string Icon { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public static async Task<AssetPickerItem> CreateAsync(IGameFile file)
    {
        Bitmap? bitmap = null;
        if (file.TypeId == "sprite")
        {
            try { var bytes = await file.ReadAllBytesAsync(); using var stream = new MemoryStream(bytes); bitmap = new Bitmap(stream); } catch { }
        }
        return new(file, System.IO.Path.GetFileName(file.Path), file.Path, file.TypeId switch { "audio" => "♪", "video" => "▶", _ => "▣" }, bitmap);
    }
}
