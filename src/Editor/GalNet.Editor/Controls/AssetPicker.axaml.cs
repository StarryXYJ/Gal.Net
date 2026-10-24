using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Core.Assets;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-only media picker. Only IDs are authored; paths and labels follow resource generations.</summary>
public partial class AssetPicker : UserControl, IDisposable
{
    public static readonly StyledProperty<string> SelectedAssetIdProperty = AvaloniaProperty.Register<AssetPicker, string>(nameof(SelectedAssetId), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<AssetPickerFilter> FilterProperty = AvaloniaProperty.Register<AssetPicker, AssetPickerFilter>(nameof(Filter));
    public static readonly StyledProperty<IAssetManager?> AssetManagerProperty = AvaloniaProperty.Register<AssetPicker, IAssetManager?>(nameof(AssetManager));
    public static readonly DirectProperty<AssetPicker, string> DisplayTextProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayText), x => x.DisplayText);
    public static readonly DirectProperty<AssetPicker, string> DisplayIconProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DisplayIcon), x => x.DisplayIcon);
    public static readonly DirectProperty<AssetPicker, string> DetailPathProperty = AvaloniaProperty.RegisterDirect<AssetPicker, string>(nameof(DetailPath), x => x.DetailPath);
    private string _displayText = "None", _displayIcon = "—", _detailPath = string.Empty;
    private readonly Dictionary<string, Task<AssetPickerItem>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _lifetime = new();
    private IEditorAssetChanges? _changes;
    private int _generation, _resolveVersion, _listVersion, _thumbnailCount;
    private bool _flyoutOpen, _attached, _disposed;
    public ObservableCollection<AssetPickerItem> Items { get; } = [];
    public string SelectedAssetId { get => GetValue(SelectedAssetIdProperty); set => SetValue(SelectedAssetIdProperty, value ?? string.Empty); }
    public AssetPickerFilter Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    public IAssetManager? AssetManager { get => GetValue(AssetManagerProperty); set => SetValue(AssetManagerProperty, value); }
    public string DisplayText => _displayText;
    public string DisplayIcon => _displayIcon;
    public string DetailPath => _detailPath;
    public ResourcePickerPanel ResourcePanel => PickerPanel;
    static AssetPicker()
    {
        SelectedAssetIdProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { _ = picker.ResolveCurrentAsync(); });
        AssetManagerProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => picker.ManagerChanged());
        FilterProperty.Changed.AddClassHandler<AssetPicker>((picker, args) => { if (picker._flyoutOpen) _ = picker.LoadItemsAsync(); });
    }
    public AssetPicker()
    {
        InitializeComponent();
        PickerPanel.Search.PlaceholderText = "Search assets";
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
        if (!_disposed) ManagerChanged();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false; Unsubscribe(); PickerButton.Flyout?.Hide(); if (!_disposed) ClearCache();
        base.OnDetachedFromVisualTree(e);
    }
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
                Display("Loading…", "…", id);
                var file = await manager.GetFileAsync(id, ct);
                if (!Current(generation, manager, revision) || version != _resolveVersion) return;
                if (file is not null) current = await ItemAsync(file);
            }
            if (!Current(generation, manager, revision) || version != _resolveVersion) return;
            Display(string.IsNullOrWhiteSpace(id) ? "None" : current?.Name ?? "Missing!", string.IsNullOrWhiteSpace(id) ? "—" : current?.Icon ?? "!", current?.Path ?? id);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(generation, manager, revision) && version == _resolveVersion) Display("Unable to load", "!", error.Message);
        }
    }
    private async Task LoadItemsAsync()
    {
        if (_disposed) return;
        var version = ++_listVersion; var generation = _generation; var manager = AssetManager;
        var revision = (manager as IEditorAssetChanges)?.Revision; var filter = Filter; var ct = _lifetime.Token;
        try
        {
            if (manager is null) { Items.Clear(); PickerPanel.SetItems([]); return; }
            var type = filter switch { AssetPickerFilter.Image => "sprite", AssetPickerFilter.Audio => "audio", AssetPickerFilter.Video => "video", AssetPickerFilter.EffectProgram => "effect-program", AssetPickerFilter.ImageVariants => "image-variants", _ => null };
            var files = await manager.GetFilesAsync(type, ct);
            if (!Current(generation, manager, revision) || version != _listVersion) return;
            if (filter == AssetPickerFilter.All) files = files.Where(file => file.TypeId is "sprite" or "audio" or "video" or "effect-program" or "image-variants").ToArray();
            else if (filter == AssetPickerFilter.Text) files = files.Where(file => IsTextPath(file.Path)).ToArray();
            var items = await Task.WhenAll(files.Select(ItemAsync));
            if (!Current(generation, manager, revision) || version != _listVersion) return;
            Items.Clear(); Items.Add(AssetPickerItem.Empty); foreach (var item in items) Items.Add(item);
            PickerPanel.Hint = "";
            PickerPanel.SetItems(Items.Select(item => new ResourcePickerItem(item.File?.Id ?? "", item.Name, item.Path, item.Icon, item.Thumbnail)).ToArray());
            _loadedRevision = revision;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(generation, manager, revision) && version == _listVersion) { Items.Clear(); PickerPanel.SetItems([]); PickerPanel.Hint = error.Message; }
        }
    }
    internal static bool IsTextPath(string path) => AssetPickerTextFileExtensions.IsSupported(path);
    private void Display(string text, string icon, string path)
    {
        SetAndRaise(DisplayTextProperty, ref _displayText, text); SetAndRaise(DisplayIconProperty, ref _displayIcon, icon); SetAndRaise(DetailPathProperty, ref _detailPath, path);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Unsubscribe(); PickerButton.Flyout?.Hide(); ClearCache(); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}

public sealed class AssetPickerItem : IDisposable
{
    public static AssetPickerItem Empty { get; } = new(null, "None", string.Empty, "×", null);
    private AssetPickerItem(IGameFile? file, string name, string path, string icon, Bitmap? thumbnail) { File = file; Name = name; Path = path; Icon = file is not null && AssetPicker.IsTextPath(path) ? "T" : icon; Thumbnail = thumbnail; }
    public IGameFile? File { get; }
    public string Name { get; }
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
