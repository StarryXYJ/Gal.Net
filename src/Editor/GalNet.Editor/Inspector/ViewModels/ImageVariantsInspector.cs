using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed partial class VariantDimensionRow(string key, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _label = "";
    partial void OnLabelChanged(string value) => changed();
}

public sealed partial class VariantPreviewRow(string key, string label, IReadOnlyList<string> options, string? value, Action<string, string> select) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public IReadOnlyList<string> Options { get; } = options;
    [ObservableProperty] private string? _value = value;
    partial void OnValueChanged(string? value) { if (value is not null) select(Key, value); }
}

/// <summary>Typed projection of a project-owned text draft. Preview state never enters content history.</summary>
public sealed partial class ImageVariantsInspector : ObservableObject, IDisposable
{
    private readonly string _root;
    private readonly IProjectService _projects;
    private readonly IResourceTypeCatalog _types;
    private readonly IAssetCatalogService _assets;
    private readonly IEditorLocalizationService _text;
    private ImageVariantsSource? _source;
    private ImageVariantsAuthoringSnapshot? _snapshot;
    private ImageVariantResolution? _selection;
    private IReadOnlyDictionary<string, string>? _request;
    private IReadOnlyList<string> _protected = [];
    private bool _refreshing, _disposed;
    private int _generation, _imageGeneration;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ProjectTextBuffer Buffer { get; }
    public Task ConfigurationSaving { get; private set; } = Task.CompletedTask;
    public Task PreviewLoading { get; private set; } = Task.CompletedTask;
    public ObservableCollection<VariantDimensionRow> Dimensions { get; } = [];
    public ObservableCollection<VariantPreviewRow> PreviewRows { get; } = [];
    public bool CanConfigure => !_disposed && _source is not null && !Buffer.IsReadOnly && _projects.Current?.RootPath == _root;
    public string? DefaultChoice { get => VariantKeys.Contains(Default, StringComparer.Ordinal) ? Default : null; set { if (!_refreshing && value is not null && VariantKeys.Contains(value, StringComparer.Ordinal)) Default = value; } }
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _default = "";
    [ObservableProperty] private IReadOnlyList<string> _variantKeys = [];
    [ObservableProperty] private string _diagnostics = "";
    [ObservableProperty] private string _requested = "";
    [ObservableProperty] private string _actual = "";
    [ObservableProperty] private string _fallback = "";
    [ObservableProperty] private Bitmap? _preview;

    public ImageVariantsInspector(string root, ProjectTextBuffer buffer, IProjectService projects, IResourceTypeCatalog types, IAssetCatalogService assets, IEditorLocalizationService text)
    {
        _root = root; Buffer = buffer; _projects = projects; _types = types; _assets = assets; _text = text;
        buffer.PropertyChanged += BufferChanged; assets.Changed += ResourcesChanged; text.PropertyChanged += LocaleChanged;
        Refresh();
    }
    private bool Current => !_disposed && _projects.Current?.RootPath == _root && !Buffer.IsReadOnly;
    partial void OnFolderChanged(string value) { if (!_refreshing && CanConfigure) { _source!.Folder = value; Write(); } }
    partial void OnDefaultChanged(string value) { OnPropertyChanged(nameof(DefaultChoice)); if (!_refreshing && CanConfigure && value is not null) { _source!.Default = value; Write(); } }
    partial void OnVariantKeysChanged(IReadOnlyList<string> value) { OnPropertyChanged(nameof(DefaultChoice)); }
    private void Write()
    {
        if (!Current || _source is null) return;
        Buffer.Text = JsonSerializer.Serialize(_source, Json);
        ConfigurationSaving = CommitConfigurationAsync(ConfigurationSaving);
    }
    private async Task CommitConfigurationAsync(Task previous)
    {
        await previous;
        if (!Current) return;
        try { await Buffer.CommitAsync(); }
        catch (Exception error) { if (Current) Diagnostics = error.Message; }
    }
    private void BufferChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(ProjectTextBuffer.Text) or nameof(ProjectTextBuffer.Path) or nameof(ProjectTextBuffer.IsReadOnly)) Refresh(); }
    private void ResourcesChanged() => Dispatcher.UIThread.Post(() => { if (Current) Refresh(); });
    private void LocaleChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]" && Current) Refresh(); }
    public void Refresh()
    {
        if (!Current) { OnPropertyChanged(nameof(CanConfigure)); return; }
        var generation = ++_generation;
        _refreshing = true; _snapshot = null; _selection = null; _source = null; _imageGeneration++; VariantKeys = [];
        PreviewRows.Clear(); Preview?.Dispose(); Preview = null; Actual = ""; Fallback = ""; Diagnostics = "";
        try
        {
            _source = ImageVariantsCodec.ReadSource(Encoding.UTF8.GetBytes(Buffer.Text));
            Folder = _source.Folder; Default = _source.Default;
            _snapshot = ImageVariantsAuthoring.Scan(_root, Buffer.Path, Buffer.Text, _types);
            VariantKeys = _snapshot.Files.Select(file => Path.GetFileNameWithoutExtension(file.FileName)).Order(StringComparer.Ordinal).ToArray();
            if (_source.Dimensions?.Any(item => item is null) == true)
            {
                Diagnostics = string.Join("\n", _snapshot.Build.Diagnostics.Select(item => $"{item.Path} [{item.Code}]: {item.Message}"));
                Dimensions.Clear(); _source = null; return;
            }
            // Even a missing default must leave the default picker and names editable.
            var definitionForNames = _snapshot.Build.Definition ?? ImageVariantsBuilder.Build(new ImageVariantsSource
            {
                Folder = _source.Folder,
                Default = VariantKeys.FirstOrDefault() ?? "",
                Dimensions = _source.Dimensions
            }, _snapshot.Files).Definition;
            var dimensions = definitionForNames?.Dimensions ?? [];
            if (!Dimensions.Select(row => row.Key).SequenceEqual(dimensions.Select(item => item.Key)))
            {
                Dimensions.Clear();
                foreach (var item in dimensions) Dimensions.Add(new(item.Key, DimensionsChanged));
            }
            for (var i = 0; i < dimensions.Count; i++)
            {
                Dimensions[i].Heading = _text.Format("Variants.Dimension", i + 1);
                Dimensions[i].Label = ConfiguredLabel(i) ?? "";
            }
            Diagnostics = string.Join("\n", _snapshot.Build.Diagnostics.Select(item => $"{item.Path} [{item.Code}]: {item.Message}"));
            if (_snapshot.Build.Definition is { } definition)
            {
                _selection = _request is null ? ImageVariantsResolver.Initialize(definition) : ImageVariantsResolver.Resolve(definition, _request, _protected);
                _request = _selection.Request;
                for (var i = 0; i < definition.Dimensions.Count; i++)
                {
                    var dimension = definition.Dimensions[i];
                    var label = ConfiguredLabel(i);
                    PreviewRows.Add(new(dimension.Key, string.IsNullOrWhiteSpace(label) ? _text.Format("Variants.Dimension", i + 1) : label,
                        dimension.Options, _request.GetValueOrDefault(dimension.Key), (key, value) => { if (generation == _generation && Current) Select(key, value); }));
                }
                ShowResult();
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or JsonException)
        { Diagnostics = error.Message; }
        finally { _refreshing = false; OnPropertyChanged(nameof(CanConfigure)); }
    }
    private string? ConfiguredLabel(int position) => _source?.Dimensions is { } names && position < names.Count ? names[position]?.Label : null;
    private void DimensionsChanged()
    {
        if (_refreshing || !CanConfigure) return;
        // Preserve dormant position names so temporarily removing the longest file is reversible.
        _source!.Dimensions = Dimensions.Select(row => new ImageVariantDimensionSource { Key = row.Key, Label = string.IsNullOrWhiteSpace(row.Label) ? null : row.Label })
            .Concat(_source.Dimensions?.Skip(Dimensions.Count) ?? []).ToList();
        Write();
    }
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (!CanConfigure || _projects.Current?.Services.GetService(typeof(IFileDialogService)) is not IFileDialogService dialogs) return;
        var generation = _generation;
        var selected = await dialogs.OpenFolderPickerAsync(_text["Variants.Folder"]);
        if (!Current || generation != _generation || string.IsNullOrWhiteSpace(selected)) return;
        try
        {
            var reader = new ProjectRawReader(_root);
            var relative = reader.Relative(selected);
            if (relative != "Assets" && !relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Image folder must be inside Assets.");
            _ = reader.Resolve(relative);
            var folder = Path.GetRelativePath(Path.GetDirectoryName(Path.Combine(_root, Buffer.Path))!, selected).Replace('\\', '/');
            if (folder == Folder) Refresh(); else Folder = folder;
            await ConfigurationSaving;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        { if (Current) Diagnostics = error.Message; }
    }
    [RelayCommand] private void ResetPreview() { if (Current) { _request = null; _protected = []; Refresh(); } }
    public void Select(string key, string value)
    {
        if (!Current || _snapshot?.Build.Definition is not { } definition || _request is null) return;
        try { _selection = ImageVariantsResolver.Select(definition, _request, key, value); _request = _selection.Request; _protected = _selection.ProtectedDimensions; ShowResult(); }
        catch (ArgumentException error) { Diagnostics = error.Message; }
    }
    private string DisplayDimension(string key) => PreviewRows.FirstOrDefault(row => row.Key == key)?.Label ?? key;
    private void ShowResult()
    {
        if (_selection is not { } result) return;
        var previous = Preview; Preview = null; previous?.Dispose();
        Requested = string.Join(" · ", result.Request.Select(pair => DisplayDimension(pair.Key) + "=" + pair.Value));
        Actual = result.Variant.Key + " · " + _text["Variants.Reason." + result.Reason];
        Fallback = string.Join("\n", result.Candidates.Select(item => string.Join(" · ", item.Selection.Select(pair => DisplayDimension(pair.Key) + "=" + pair.Value)) + " → " + (item.Match?.Key ?? "∅")));
        if (_snapshot!.ImagePaths.TryGetValue(result.Variant.AssetId, out var path)) PreviewLoading = LoadPreviewAsync(path, ++_imageGeneration);
    }
    private async Task LoadPreviewAsync(string path, int generation)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(new ProjectRawReader(_root).Resolve(path));
            using var stream = new MemoryStream(bytes); var bitmap = Bitmap.DecodeToWidth(stream, 480);
            if (!Current || generation != _imageGeneration) { bitmap.Dispose(); return; }
            var old = Preview; Preview = bitmap; old?.Dispose();
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
        { if (Current && generation == _imageGeneration) Diagnostics = error.Message; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _generation++; _imageGeneration++;
        Buffer.PropertyChanged -= BufferChanged; _assets.Changed -= ResourcesChanged; _text.PropertyChanged -= LocaleChanged;
        Preview?.Dispose(); Preview = null;
    }
}
