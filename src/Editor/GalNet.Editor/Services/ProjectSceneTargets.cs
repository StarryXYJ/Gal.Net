using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Scene;
using GalNet.Core.Variants;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>One immutable author snapshot plus asynchronously loaded resource declarations per project scope.</summary>
public sealed class ProjectSceneTargets : IDisposable
{
    private readonly IEditorDocumentService _documents;
    private readonly IAssetManager? _assets;
    private readonly IEffectCatalog? _effects;
    private readonly IAssetCatalogService? _resources;
    private readonly IProjectContentSession? _content;
    private readonly IProjectService? _projects;
    private readonly IResourceTypeCatalog? _types;
    private string _workingFingerprint = "";
    private readonly object _gate = new();
    private CancellationTokenSource _resourceLifetime = new();
    private bool _disposed;
    private string? _snapshot;
    private EditorProjectDocument? _frozen;
    private SceneTargetAnalysis? _analysis;
    private IReadOnlyDictionary<string, ShaderEffectDescriptor?> _programs = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, ImageVariantsDefinition?> _variants = new Dictionary<string, ImageVariantsDefinition?>(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;
    private long _resourceEpoch;
    public event Action? Changed;
    public long Revision { get; private set; }
    public bool IsAvailable { get { lock (_gate) return !_disposed && _documents.IsLoaded; } }
    internal (object? Author, long Resources) PreparationStamp { get { lock (_gate) return (Capture() ? _frozen : null, _resourceEpoch); } }

    public ProjectSceneTargets(IEditorDocumentService documents, IAssetManager? assets = null,
        IEffectCatalog? effects = null, IAssetCatalogService? resources = null, IProjectContentSession? content = null,
        IProjectService? projects = null, IResourceTypeCatalog? types = null)
    {
        _documents = documents; _assets = assets; _effects = effects; _resources = resources;
        _content = content; _projects = projects; _types = types;
        if (_content is not null) _content.Changed += OnContentChanged;
        if (_resources is not null) _resources.Changed += OnResourcesChanged;
    }
    public ImageVariantsDefinition? VariantDefinition(string id)
    {
        lock (_gate) { if (!Capture()) return null; QueuePreparation(); return _variants.GetValueOrDefault(id.Trim()); }
    }
    private void OnContentChanged()
    {
        var fingerprint = string.Join("\n", _content!.FileDocuments.Values.Where(file => file.Exists && file.ManagedContent && file.Path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Id, StringComparer.Ordinal).Select(file => file.Id + ":" + file.Path + ":" + Convert.ToBase64String(file.Data)));
        if (fingerprint == _workingFingerprint) return;
        _workingFingerprint = fingerprint; InvalidateResources();
    }

    public SceneTargetResult Before(string groupId, string? entryId) => Query(groupId, entryId, null);
    public SceneTargetResult AtTimeline(string groupId, string entryId, SceneTimelinePosition position) => Query(groupId, entryId, position);
    private SceneTargetResult Query(string groupId, string? entryId, SceneTimelinePosition? position)
    {
        lock (_gate)
        {
            if (!Capture()) return new([], false, false);
            QueuePreparation();
            _analysis ??= new(_frozen!.Graph, _frozen.GroupEntries, programs: _assets is null ? null : _programs, effects: _effects, variants: _variants);
            return position is null ? _analysis.Before(groupId, entryId) : entryId is null ? new([], false, false) : _analysis.AtTimeline(groupId, entryId, position);
        }
    }

    // Only the caller's wait is cancelled; resource IO belongs to the project/refresh generation.
    public async Task PrepareAsync(CancellationToken token = default)
    {
        while (true)
        {
            Task? loading;
            lock (_gate) { if (!Capture()) return; loading = QueuePreparation(); }
            if (loading is null) return;
            await loading.WaitAsync(token);
        }
    }

    private bool Capture()
    {
        if (_disposed) return false;
        if (!_documents.IsLoaded)
        {
            if (_snapshot is not null) Revision++;
            _snapshot = null; _frozen = null; _analysis = null; return false;
        }
        var snapshot = JsonSerializer.Serialize(new EditorProjectDocument
        {
            Graph = _documents.CurrentDocument,
            GroupEntries = _documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries)
        });
        if (snapshot != _snapshot)
        {
            _frozen = JsonSerializer.Deserialize<EditorProjectDocument>(snapshot)!;
            _snapshot = snapshot; _analysis = null; Revision++;
        }
        return true;
    }

    private Task? QueuePreparation()
    {
        if (_assets is null || _frozen is null || _disposed) return null;
        if (_loading is not null) return _loading;
        var ids = Programs(_frozen).Distinct(StringComparer.Ordinal).Take(256).Where(id => !_programs.ContainsKey(id)).ToArray();
        var variants = Variants(_frozen).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).Where(id => !_variants.ContainsKey(id)).ToArray();
        if (ids.Length == 0 && variants.Length == 0) return null;
        var epoch = _resourceEpoch;
        var token = _resourceLifetime.Token;
        var working = _content?.FileDocuments.Values.Where(file => file.Exists && file.ManagedContent && file.Path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => file.Path, file => file.Data.ToArray(), StringComparer.OrdinalIgnoreCase) ?? [];
        var root = _projects?.Current?.RootPath;
        return _loading = Task.Run(() => LoadAsync(ids, variants, epoch, token, working, root));
    }

    private async Task LoadAsync(string[] ids, string[] variants, long epoch, CancellationToken token, Dictionary<string, byte[]> working, string? root)
    {
        var loaded = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.Ordinal);
        var loadedVariants = new Dictionary<string, ImageVariantsDefinition?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var id in ids)
            {
                token.ThrowIfCancellationRequested();
                loaded[id] = null;
                try
                {
                    var file = await _assets!.GetFileAsync(id, token);
                    if (file is null || file.TypeId != "effect-program" || file.Length > 1024 * 1024) continue;
                    var bytes = await file.ReadAllBytesAsync(token);
                    if (bytes.Length > 1024 * 1024) continue;
                    var source = new UTF8Encoding(false, true).GetString(bytes);
                    var parsed = ShaderEffectMetadataParser.Parse(new EffectProgramResource(id), source);
                    if (parsed.Success) loaded[id] = parsed.Descriptor;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { /* Missing/unreadable declarations remain incomplete until the next resource refresh. */ }
            }
            foreach (var id in variants)
            {
                token.ThrowIfCancellationRequested();
                loadedVariants[id] = null;
                try
                {
                    var file = await _assets!.GetFileAsync(id, token);
                    if (file is null || file.TypeId != ImageVariantsCodec.ResourceType || file.Length > 1024 * 1024) continue;
                    if (root is not null && _types is not null && working.TryGetValue("Assets/" + file.Path, out var author))
                    {
                        if (author.Length <= 1024 * 1024 && ProjectTextFiles.TryDecode(author, out _, out var text))
                            loadedVariants[id] = ImageVariantsAuthoring.Scan(root, "Assets/" + file.Path, text, _types).Build.Definition;
                        continue;
                    }
                    var bytes = await file.ReadAllBytesAsync(token);
                    if (bytes.Length <= 1024 * 1024) loadedVariants[id] = ImageVariantsCodec.Decode(bytes);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
                { /* Failed definitions stay incomplete until the next resource refresh. */ }
            }
            lock (_gate)
            {
                if (_disposed || epoch != _resourceEpoch || _frozen is null) return;
                var referenced = Programs(_frozen!).Distinct(StringComparer.Ordinal).Take(256).ToHashSet(StringComparer.Ordinal);
                var merged = _programs.Where(pair => referenced.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var pair in loaded.Where(pair => referenced.Contains(pair.Key))) merged[pair.Key] = pair.Value;
                var referencedVariants = Variants(_frozen).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var mergedVariants = _variants.Where(pair => referencedVariants.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in loadedVariants.Where(pair => referencedVariants.Contains(pair.Key))) mergedVariants[pair.Key] = pair.Value;
                _programs = merged; _variants = mergedVariants; _analysis = null; Revision++;
            }
            NotifyChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lock (_gate) { if (epoch == _resourceEpoch) _loading = null; } }
    }

    private static IEnumerable<string> Variants(EditorProjectDocument project) => project.GroupEntries.Values.SelectMany(entries => entries)
        .Where(entry => entry.Type == BuiltinEntryIds.ShowVariants).Select(entry => entry.Parameters.GetValueOrDefault("assetId"))
        .Where(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())).Select(value => value.GetString()!.Trim());

    private static IEnumerable<string> Programs(EditorProjectDocument project)
    {
        foreach (var entry in project.GroupEntries.Values.SelectMany(entries => entries))
        {
            if (entry.Type == "effect.apply" && entry.Parameters.TryGetValue("program", out var program)
                && program.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(program.GetString())) yield return program.GetString()!;
            if (entry.Type != "timeline.play" || !entry.Parameters.TryGetValue("timeline", out var timeline) || timeline.ValueKind != JsonValueKind.Object) continue;
            var events = Member(timeline, "events");
            if (events.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in events.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || Member(item, "type") is not { ValueKind: JsonValueKind.String } type || type.GetString() != "effect.apply") continue;
                var parameters = Member(item, "parameters");
                if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("program", out program)
                    && program.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(program.GetString())) yield return program.GetString()!;
            }
        }
    }

    private static JsonElement Member(JsonElement element, string name) => element.EnumerateObject()
        .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private void OnResourcesChanged() => Dispatcher.UIThread.Post(InvalidateResources);
    public void InvalidateResources()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _resourceEpoch++; _resourceLifetime.Cancel(); _resourceLifetime.Dispose(); _resourceLifetime = new();
            _programs = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.Ordinal); _loading = null; _analysis = null; Revision++;
            _variants = new Dictionary<string, ImageVariantsDefinition?>(StringComparer.OrdinalIgnoreCase);
            if (Capture()) QueuePreparation();
        }
        NotifyChanged();
    }
    private void NotifyChanged() => Dispatcher.UIThread.Post(() => { if (IsAvailable) Changed?.Invoke(); });
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _resourceEpoch++; _resourceLifetime.Cancel(); _resourceLifetime.Dispose();
            if (_resources is not null) _resources.Changed -= OnResourcesChanged;
            if (_content is not null) _content.Changed -= OnContentChanged;
            _snapshot = null; _frozen = null; _analysis = null; _programs = new Dictionary<string, ShaderEffectDescriptor?>(); Revision++; Changed = null;
            _variants = new Dictionary<string, ImageVariantsDefinition?>();
        }
    }
}
