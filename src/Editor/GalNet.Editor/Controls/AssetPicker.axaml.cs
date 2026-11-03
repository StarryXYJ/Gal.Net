using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-only media picker. Only IDs are authored; paths and labels follow resource generations.</summary>
public partial class AssetPicker : UserControl, IDisposable
{
    public static readonly StyledProperty<string> SelectedAssetIdProperty = AvaloniaProperty.Register<AssetPicker, string>(nameof(SelectedAssetId), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<AssetPickerFilter> FilterProperty = AvaloniaProperty.Register<AssetPicker, AssetPickerFilter>(nameof(Filter));
    public static readonly StyledProperty<IAssetManager?> AssetManagerProperty = AvaloniaProperty.Register<AssetPicker, IAssetManager?>(nameof(AssetManager));
    public static readonly StyledProperty<string?> ResourceTypeIdProperty = AvaloniaProperty.Register<AssetPicker, string?>(nameof(ResourceTypeId));
    public static readonly DirectProperty<AssetPicker, string> DisplayTextProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayText), x => x.DisplayText);
    public static readonly DirectProperty<AssetPicker, string> DisplayIconProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayIcon), x => x.DisplayIcon);
    public static readonly DirectProperty<AssetPicker, string> DetailPathProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DetailPath), x => x.DetailPath);
    private string _displayText = "", _displayIcon = "—", _detailPath = string.Empty;
    private readonly Dictionary<string, Task<AssetPickerItem>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _lifetime = new();
    private IEditorAssetChanges? _changes;
    private IEditorLocalizationService? _localization;
    private string? _displayTextKey, _pickerHintKey, _pickerHintDetails;
    private int _generation, _resolveVersion, _listVersion, _thumbnailCount;
    private bool _flyoutOpen, _attached, _disposed;
    public ObservableCollection<AssetPickerItem> Items { get; } = [];
    public string SelectedAssetId { get => GetValue(SelectedAssetIdProperty); set => SetValue(SelectedAssetIdProperty, value ?? string.Empty); }
    public AssetPickerFilter Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    public IAssetManager? AssetManager { get => GetValue(AssetManagerProperty); set => SetValue(AssetManagerProperty, value); }
    public string? ResourceTypeId { get => GetValue(ResourceTypeIdProperty); set => SetValue(ResourceTypeIdProperty, value); }
    public string DisplayText => _displayText;
    public string DisplayIcon => _displayIcon;
    public string DetailPath => _detailPath;
    public ResourcePickerPanel ResourcePanel => PickerPanel;
    static AssetPicker()
    {
        SelectedAssetIdProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { _ = picker.ResolveCurrentAsync(); });
        AssetManagerProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => picker.ManagerChanged());
        FilterProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { if (picker._flyoutOpen) _ = picker.LoadItemsAsync(); });
        ResourceTypeIdProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { if (picker._flyoutOpen) _ = picker.LoadItemsAsync(); });
    }
    public AssetPicker()
    {
        InitializeComponent();
        _localization = EditorLocalizationService.Current;
        ApplyLocalizedText();
        DisplayLocalized("AssetPicker.None", "—", string.Empty);
        PickerPanel.Confirmed += () =>
        {
            if (PickerPanel.Selected is not { } item || _disposed) return;
            // The source may already have changed before its UI notification was processed.
            if (_loadedRevision != (AssetManager as IEditorAssetChanges)?.Revision) { RefreshResources(); return; }
            SelectedAssetId = item.Id; PickerButton.Flyout?.Hide();
        };
        PickerPanel.Cancelled += () => PickerButton.Flyout?.Hide();
    }
    private long? _loadedRevision;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _attached = true;
        _localization = EditorLocalizationService.Current;
        if (_localization is not null) _localization.PropertyChanged += OnLocalizationChanged;
        ApplyLocalizedText();
        if (!_disposed) ManagerChanged();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_localization is not null) _localization.PropertyChanged -= OnLocalizationChanged;
        _localization = null;
        _attached = false; Unsubscribe(); PickerButton.Flyout?.Hide(); if (!_disposed) ClearCache();
        base.OnDetachedFromVisualTree(e);
    }
    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]") ApplyLocalizedText();
    }
    private string Text(string key) => _localization?[key] ?? "[" + key + "]";
    private void ApplyLocalizedText()
    {
        PickerPanel.Search.PlaceholderText = Text("AssetPicker.Search");
        ApplyPickerHint();
        if (_displayTextKey is { } displayKey)
            Display(Text(displayKey), _displayIcon, _detailPath, displayKey);
        if (Items.Count > 0) RefreshPickerItems();
    }
    private void SetPickerHint(string? key, string? details = null)
    {
        _pickerHintKey = key; _pickerHintDetails = details;
        ApplyPickerHint();
    }
    private void ApplyPickerHint() => PickerPanel.Hint = _pickerHintKey is { } hintKey
        ? _pickerHintDetails is { Length: > 0 } details ? Text(hintKey) + Environment.NewLine + details : Text(hintKey)
        : string.Empty;
    private void RefreshPickerItems() => PickerPanel.SetItems(Items.Select(item => new ResourcePickerItem(
        item.File?.Id ?? "", item.File is null ? Text("AssetPicker.None") : item.Name, item.Path, item.Icon, item.Thumbnail)).ToArray());
    private void Unsubscribe() { if (_changes is not null) _changes.Changed -= OnAssetChanges; _changes = null; }
    private void ManagerChanged()
    {
        if (_disposed) return;
        Unsubscribe();
        if (_attached && AssetManager is IEditorAssetChanges changes) { _changes = changes; changes.Changed += OnAssetChanges; }
        RefreshResources();
    }
    private void OnAssetChanges() => Dispatcher.UIThread.Post(() => { if (!_disposed && _attached) RefreshResources(); });
    private void RefreshResources()
    {
        ClearCache();
        if (_disposed) return;
        _ = ResolveCurrentAsync();
        if (_flyoutOpen) _ = LoadItemsAsync();
    }
    private void ClearCache()
    {
        _generation++; _resolveVersion++; _listVersion++; _loadedRevision = null;
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        Items.Clear(); PickerPanel.SetItems([]);
        foreach (var task in _cache.Values) if (task.IsCompletedSuccessfully) task.Result.Dispose();
        _cache.Clear(); _thumbnailCount = 0;
    }
    private async void OnFlyoutOpened(object? sender, EventArgs e)
    {
        _flyoutOpen = true; await LoadItemsAsync();
        if (_flyoutOpen && !_disposed) Dispatcher.UIThread.Post(() => { if (_flyoutOpen && !_disposed) PickerPanel.Search.Focus(); });
    }
    private void OnFlyoutClosed(object? sender, EventArgs e) { _flyoutOpen = false; _listVersion++; }
    private bool Current(int generation, IAssetManager? manager, long? revision) => !_disposed && generation == _generation
        && ReferenceEquals(manager, AssetManager) && revision == (manager as IEditorAssetChanges)?.Revision;
    private Task<AssetPickerItem> ItemAsync(IGameFile file)
    {
        if (_cache.TryGetValue(file.Id, out var cached)) return cached;
        var thumbnail = file.TypeId == "sprite" && _thumbnailCount++ < 128;
        return _cache[file.Id] = CreateItemAsync(file, thumbnail, _generation, _lifetime.Token);
    }
    private async Task<AssetPickerItem> CreateItemAsync(IGameFile file, bool thumbnail, int generation, CancellationToken ct)
    {
        var item = await AssetPickerItem.CreateAsync(file, thumbnail, ct);
        if (_disposed || generation != _generation) { item.Dispose(); throw new OperationCanceledException(); }
        return item;
    }
    private async Task ResolveCurrentAsync()
    {
        if (_disposed) return;
        var version = ++_resolveVersion; var generation = _generation; var manager = AssetManager;
        var revision = (manager as IEditorAssetChanges)?.Revision; var id = SelectedAssetId; var ct = _lifetime.Token;
        try
        {
            AssetPickerItem? current = null;
            if (!string.IsNullOrWhiteSpace(id) && manager is not null)
            {
                DisplayLocalized("AssetPicker.Loading", "…", id);
                var file = await manager.GetFileAsync(id, ct);
                if (!Current(generation, manager, revision) || version != _resolveVersion) return;
                if (file is not null) current = await ItemAsync(file);
            }
            if (!Current(generation, manager, revision) || version != _resolveVersion) return;
            if (string.IsNullOrWhiteSpace(id)) DisplayLocalized("AssetPicker.None", "—", string.Empty);
            else if (current is not null) Display(current.Name, current.Icon, current.Path);
            else DisplayLocalized("AssetPicker.Missing", "!", id);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(generation, manager, revision) && version == _resolveVersion) DisplayLocalized("AssetPicker.LoadFailed", "!", error.Message);
        }
    }
    private async Task LoadItemsAsync()
    {
        if (_disposed) return;
        var version = ++_listVersion; var generation = _generation; var manager = AssetManager;
        var revision = (manager as IEditorAssetChanges)?.Revision; var filter = Filter; var ct = _lifetime.Token;
        try
        {
            if (manager is null) { Items.Clear(); PickerPanel.SetItems([]); SetPickerHint(null); return; }
            var type = ResourceTypeId is { } declared ? declared == "*" ? null : declared : filter switch { AssetPickerFilter.Image => "sprite", AssetPickerFilter.Audio => "audio", AssetPickerFilter.Video => "video", AssetPickerFilter.EffectProgram => "effect-program", AssetPickerFilter.ImageVariants => "image-variants", _ => null };
            var files = await manager.GetFilesAsync(type, ct);
            if (!Current(generation, manager, revision) || version != _listVersion) return;
            if (ResourceTypeId is not null) files = files.Where(file => file.TypeId is not ("graph" or "group")).ToArray();
            else if (filter == AssetPickerFilter.All) files = files.Where(file => file.TypeId is "sprite" or "audio" or "video" or "effect-program" or "image-variants").ToArray();
            else if (filter == AssetPickerFilter.Text) files = files.Where(file => IsTextPath(file.Path)).ToArray();
            var items = await Task.WhenAll(files.Select(ItemAsync));
            if (!Current(generation, manager, revision) || version != _listVersion) return;
            Items.Clear(); Items.Add(AssetPickerItem.Empty); foreach (var item in items) Items.Add(item);
            SetPickerHint(null);
            RefreshPickerItems();
            _loadedRevision = revision;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(generation, manager, revision) && version == _listVersion) { Items.Clear(); PickerPanel.SetItems([]); SetPickerHint("AssetPicker.LoadFailed", error.Message); }
        }
    }
    internal static bool IsTextPath(string path) => AssetPickerTextFileExtensions.IsSupported(path);
    private void DisplayLocalized(string key, string icon, string path) => Display(Text(key), icon, path, key);
    private void Display(string text, string icon, string path, string? localizedKey = null)
    {
        _displayTextKey = localizedKey;
        SetAndRaise(DisplayTextProperty, ref _displayText, text); SetAndRaise(DisplayIconProperty, ref _displayIcon, icon); SetAndRaise(DetailPathProperty, ref _detailPath, path);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_localization is not null) _localization.PropertyChanged -= OnLocalizationChanged;
        _localization = null;
        _disposed = true; Unsubscribe(); PickerButton.Flyout?.Hide(); ClearCache(); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}

public sealed class AssetPickerItem : IDisposable
{
    public static AssetPickerItem Empty { get; } = new(null, string.Empty, string.Empty, "×", null);
    private readonly string _name;
    private AssetPickerItem(IGameFile? file, string name, string path, string icon, Bitmap? thumbnail) { File = file; _name = name; Path = path; Icon = file is not null && AssetPicker.IsTextPath(path) ? "T" : icon; Thumbnail = thumbnail; }
    public IGameFile? File { get; }
    public string Name => File is null ? EditorLocalizationService.Current?["AssetPicker.None"] ?? "[AssetPicker.None]" : _name;
    public string Path { get; }
    public string Icon { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public static async Task<AssetPickerItem> CreateAsync(IGameFile file, bool thumbnail = true, CancellationToken ct = default)
    {
        Bitmap? bitmap = null;
        if (thumbnail && file.TypeId == "sprite")
        {
            try { var bytes = await file.ReadAllBytesAsync(ct); using var stream = new MemoryStream(bytes); bitmap = Bitmap.DecodeToWidth(stream, 128); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return new(file, System.IO.Path.GetFileName(file.Path), file.Path, file.TypeId switch { "audio" => "♪", "video" => "▶", _ => "▣" }, bitmap);
    }
    public void Dispose() => Thumbnail?.Dispose();
}
