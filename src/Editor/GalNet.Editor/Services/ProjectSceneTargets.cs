using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>One immutable author snapshot plus asynchronously loaded resource declarations per project scope.</summary>
public sealed class ProjectSceneTargets : IDisposable, IEntryParameterMetadataSource
{
    private readonly IEditorDocumentService _documents;
    private readonly IAssetManager? _assets;
    private readonly IEffectCatalog? _effects;
    public ISEffectCatalog AudioCatalog { get; }
    private AudioConfiguration? _audioConfiguration;
    private string _audioConfigurationFingerprint = "";
    private readonly IAssetCatalogService? _resources;
    private readonly IProjectContentSession? _content;
    private readonly IProjectService? _projects;
    private readonly IResourceTypeCatalog? _types;
    private readonly AuthorChangeJournal? _changes;
    private readonly UndoRedoHistory? _history;
    private string _workingFingerprint = "";
    private readonly object _gate = new();
    private CancellationTokenSource _resourceLifetime = new();
    private bool _disposed;
    private string? _snapshot;
    private EditorProjectDocument? _frozen;
    private SceneTargetAnalysis? _analysis;
    private SceneTargetAnalysis? _previousAnalysis;
    private bool _captureDirty = true;
    private object? _documentIdentity;
    private AuthorChangeSet? _pendingChange;
    private readonly Dictionary<(string Group, string? Entry, SceneTimelinePosition? Position), SceneTargetResult> _queries = [];
    private readonly Dictionary<(string Group, string? Entry, SceneTimelinePosition? Position), EditorEntryData?> _queryOwners = [];
    private readonly Dictionary<(string Group, string? Entry, SceneTimelinePosition? Position), EditorEntryData?> _querySources = [];
    private long _queryRevision;
    public int AuthorCaptureCount { get; private set; }
    public int AnalysisEntryTransferCount { get { lock (_gate) return _analysis?.EntryTransferCount ?? 0; } }
    private IReadOnlyDictionary<string, ShaderEffectDescriptor?> _programs = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, ImageVariantsDefinition?> _variants = new Dictionary<string, ImageVariantsDefinition?>(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;
    private long _resourceEpoch;
    internal long ResourceEpoch { get { lock (_gate) return _resourceEpoch; } }
    public event Action? Changed;
    public long Revision { get; private set; }
    public bool IsAvailable { get { lock (_gate) return !_disposed && _documents.IsLoaded; } }
    internal (long Author, long Resources) PreparationStamp(string group, string entry, SceneTimelinePosition? position)
    { lock (_gate) return (Query(group, entry, position).AuthorRevision, _resourceEpoch); }

    public ProjectSceneTargets(IEditorDocumentService documents, IAssetManager? assets = null,
        IEffectCatalog? effects = null, IAssetCatalogService? resources = null, IProjectContentSession? content = null,
        IProjectService? projects = null, IResourceTypeCatalog? types = null, EditorHistories? histories = null, ISEffectCatalog? audioEffects = null)
    {
        _documents = documents; _assets = assets; _effects = effects; AudioCatalog = audioEffects ?? SEffectCatalog.Builtins; _resources = resources;
        _content = content; _projects = projects; _types = types;
        _history = histories?.Content; _changes = _history?.AuthorChanges;
        if (_changes is not null) _changes.Committed += OnAuthorCommitted;
        _documents.DocumentChanged += OnDocumentChanged;
        if (_content is not null) _content.Changed += OnContentChanged;
        if (_resources is not null) _resources.Changed += OnResourcesChanged;
    }
    private void OnAuthorCommitted(AuthorChangeSet change)
    {
        bool changed;
        lock (_gate) { _captureDirty = true; _pendingChange = change; if (!Capture(true)) return; QueuePreparation(); changed = RefreshQueries(); }
        if (changed) NotifyChanged();
    }
    private void OnDocumentChanged()
    {
        lock (_gate)
        {
            _captureDirty = true; _pendingChange = null;
            if (_history?.IsBusy == true) return;
            Capture(); RefreshQueries();
        }
        NotifyChanged();
    }
    public ImageVariantsDefinition? VariantDefinition(string id)
    {
        lock (_gate) { if (!Capture()) return null; QueuePreparation(); return _variants.GetValueOrDefault(id.Trim()); }
    }
    public ShaderEffectDescriptor? ProgramDescriptor(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate) { if (!Capture()) return null; QueuePreparation(); return _programs.GetValueOrDefault(id.Trim()); }
    }
    EntryParameterMetadata IEntryParameterMetadataSource.Capture(EditorProjectDocument document)
    {
        lock (_gate)
        {
            if (!Capture()) return new(Revision: Revision);
            QueuePreparation();
            // Capture declarations and revision together; a completed load must not relabel an older snapshot.
            return new(_effects, AudioCatalog, EntryParameterMetadata.ProgramIds(document).Take(256)
                .ToDictionary(id => id, id => _programs.GetValueOrDefault(id.Trim()), StringComparer.Ordinal), Revision);
        }
    }
    private void OnContentChanged()
    {
        lock (_gate) RefreshAudioConfiguration();
        var fingerprint = string.Join("\n", _content!.FileDocuments.Values.Where(file => file.Exists && file.ManagedContent && file.Path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Id, StringComparer.Ordinal).Select(file => file.Id + ":" + file.Path + ":" + Convert.ToBase64String(file.Data)));
        if (fingerprint == _workingFingerprint) return;
        _workingFingerprint = fingerprint; InvalidateResources();
    }

    public SceneTargetResult Before(string groupId, string? entryId) => Query(groupId, entryId, null);
    internal SceneTargetResult InspectBefore(string groupId, string entryId) => Query(groupId, entryId, null, retain: false);
    public SceneTargetResult AtTimeline(string groupId, string entryId, SceneTimelinePosition position) => Query(groupId, entryId, position);
    public IReadOnlyList<SceneReferenceDiagnostic> Diagnostics()
    {
        lock (_gate)
        {
            if (!Capture()) return [];
            QueuePreparation();
            EnsureAnalysis();
            return SceneReferenceAnalysis.Diagnose(new SceneReferenceIndex(_frozen!.GroupEntries), _analysis).Select(diagnostic => diagnostic with { Revision = Revision }).ToArray();
        }
    }
    private SceneTargetResult Query(string groupId, string? entryId, SceneTimelinePosition? position, bool retain = true)
    {
        lock (_gate)
        {
            if (!Capture()) return new([], false, false);
            QueuePreparation();
            RefreshAudioConfiguration();
            EnsureAnalysis();
            var key = (groupId, entryId, position);
            if (!retain) return Evaluate(key);
            if (!_queries.ContainsKey(key) && _queries.Count >= 256) { var oldest = _queries.Keys.First(); _queries.Remove(oldest); _queryOwners.Remove(oldest); _querySources.Remove(oldest); }
            return UpdateQuery(key);
        }
    }
    private void RefreshAudioConfiguration()
    {
        if (_content is null || _disposed || !_documents.IsLoaded) return;
        var options = (_projects?.Current?.Configuration ?? _content.CaptureWorking().Configuration).ReadDefaultOptions();
        var fingerprint = JsonSerializer.Serialize(new { options.AudioTracks, options.AudioBuses });
        if (fingerprint == _audioConfigurationFingerprint) return;
        _audioConfigurationFingerprint = fingerprint;
        try { _audioConfiguration = new(options.AudioTracks, options.AudioBuses); }
        catch (InvalidDataException) { _audioConfiguration = AudioConfiguration.Empty; }
        _analysis = null; _previousAnalysis = null;
        Revision++;
        RefreshQueries();
        NotifyChanged();
    }
    private void EnsureAnalysis()
    {
        if (_analysis is not null || _frozen is null) return;
        _analysis = new(_frozen.Graph, _frozen.GroupEntries, programs: _assets is null ? null : _programs, effects: _effects, variants: _variants, previousAnalysis: _previousAnalysis, audioConfiguration: _audioConfiguration, audioCatalog: AudioCatalog);
        _previousAnalysis = null;
    }
    private SceneTargetResult Evaluate((string Group, string? Entry, SceneTimelinePosition? Position) key) => key.Position is null
        ? _analysis!.Before(key.Group, key.Entry) : key.Entry is null ? new([], false, false) : _analysis!.AtTimeline(key.Group, key.Entry, key.Position);
    private bool RefreshQueries(bool author = true)
    {
        if (_queries.Count == 0) return true;
        var changed = false;
        EnsureAnalysis();
        foreach (var key in _queries.Keys.ToArray())
        {
            var previous = _queries[key];
            var next = UpdateQuery(key, author);
            changed |= previous.AudioRevision != next.AudioRevision || previous.Revision != next.Revision || previous.ContextRevision != next.ContextRevision || previous.AuthorRevision != next.AuthorRevision;
        }
        return changed;
    }
    private SceneTargetResult UpdateQuery((string Group, string? Entry, SceneTimelinePosition? Position) key, bool author = true)
    {
        var next = _analysis is null ? new SceneTargetResult([], false, false) : Evaluate(key);
        var owner = _frozen?.GroupEntries.GetValueOrDefault(key.Group)?.FirstOrDefault(entry => entry.StableId == key.Entry);
        var source = _documents.Groups.GetValueOrDefault(key.Group)?.Entries.FirstOrDefault(entry => entry.StableId == key.Entry);
        _queries.TryGetValue(key, out var previous);
        var version = previous is not null && SceneTargetAnalysis.Equivalent(previous, next) ? previous.Revision : ++_queryRevision;
        var audioVersion = previous is not null && SceneTargetAnalysis.EquivalentAudio(previous, next) ? previous.AudioRevision : ++_queryRevision;
        var context = previous is not null && ReferenceEquals(_querySources.GetValueOrDefault(key), source) && SceneTargetAnalysis.SameEntry(_queryOwners.GetValueOrDefault(key), owner) ? previous.ContextRevision : ++_queryRevision;
        var dependency = previous is not null && (!author || version == previous.Revision && context == previous.ContextRevision) ? previous.AuthorRevision : ++_queryRevision;
        _queryOwners[key] = owner; _querySources[key] = source;
        return _queries[key] = next with { Revision = version, AudioRevision = audioVersion, ContextRevision = context, AuthorRevision = dependency };
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

    private bool Capture(bool committed = false)
    {
        if (_disposed) return false;
        if (!_documents.IsLoaded)
        {
            if (_snapshot is not null) Revision++;
            _snapshot = null; _frozen = null; _analysis = null; _previousAnalysis = null; _queries.Clear(); _queryOwners.Clear(); _querySources.Clear(); _captureDirty = true; return false;
        }
        // Managed projects publish mutations through the author journal/document notification. Standalone callers retain the legacy snapshot fallback.
        if (_changes is not null && !_captureDirty) return true;
        if (!committed && _history?.IsBusy == true && _frozen is not null) return true;
        if (_changes is not null && ReferenceEquals(_documentIdentity, _documents.CurrentDocument) && _frozen is not null && _pendingChange is { UnknownScope: false } change)
        {
            var groups = _frozen.GroupEntries.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var group in change.Groups)
                if (_documents.Groups.TryGetValue(group.GroupId, out var source)) groups[group.GroupId] = JsonSerializer.Deserialize<List<EditorEntryData>>(JsonSerializer.Serialize(source.Entries))!;
                else groups.Remove(group.GroupId);
            _frozen = new() { Graph = _frozen.Graph, GroupEntries = groups };
            _snapshot = null; _previousAnalysis = _analysis; _analysis = null; _captureDirty = false; _pendingChange = null; AuthorCaptureCount++; Revision++;
            return true;
        }
        var snapshot = JsonSerializer.Serialize(new EditorProjectDocument
        {
            Graph = _documents.CurrentDocument,
            GroupEntries = _documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries)
        });
        if (snapshot != _snapshot)
        {
            _frozen = JsonSerializer.Deserialize<EditorProjectDocument>(snapshot)!;
            _snapshot = snapshot; _previousAnalysis = _analysis; _analysis = null; AuthorCaptureCount++; Revision++;
        }
        _captureDirty = false; _pendingChange = null; _documentIdentity = _documents.CurrentDocument;
        return true;
    }

    private Task? QueuePreparation()
    {
        if (_assets is null || _frozen is null || _disposed) return null;
        if (_loading is not null) return _loading;
        var ids = EntryParameterMetadata.ProgramIds(_frozen).Take(256).Where(id => !_programs.ContainsKey(id)).ToArray();
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
                    var bytes = working.TryGetValue("Assets/" + file.Path, out var author) ? author : await file.ReadAllBytesAsync(token);
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
                var referenced = EntryParameterMetadata.ProgramIds(_frozen!).Take(256).ToHashSet(StringComparer.Ordinal);
                var merged = _programs.Where(pair => referenced.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var pair in loaded.Where(pair => referenced.Contains(pair.Key))) merged[pair.Key] = pair.Value;
                var referencedVariants = Variants(_frozen).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var mergedVariants = _variants.Where(pair => referencedVariants.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in loadedVariants.Where(pair => referencedVariants.Contains(pair.Key))) mergedVariants[pair.Key] = pair.Value;
                _programs = merged; _variants = mergedVariants; _analysis = null; _previousAnalysis = null; Revision++; RefreshQueries(false);
            }
            NotifyChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lock (_gate) { if (epoch == _resourceEpoch) _loading = null; } }
    }

    private static IEnumerable<string> Variants(EditorProjectDocument project) => project.GroupEntries.Values.SelectMany(entries => entries)
        .Where(entry => entry.Type == BuiltinEntryIds.ShowVariants).Select(entry => entry.Parameters.GetValueOrDefault("assetId"))
        .Where(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())).Select(value => value.GetString()!.Trim());

    private void OnResourcesChanged() => Dispatcher.UIThread.Post(InvalidateResources);
    public void InvalidateResources()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _resourceEpoch++; _resourceLifetime.Cancel(); _resourceLifetime.Dispose(); _resourceLifetime = new();
            _programs = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.Ordinal); _loading = null; _analysis = null; _previousAnalysis = null; Revision++;
            _variants = new Dictionary<string, ImageVariantsDefinition?>(StringComparer.OrdinalIgnoreCase);
            if (Capture()) { QueuePreparation(); RefreshQueries(false); }
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
            if (_changes is not null) _changes.Committed -= OnAuthorCommitted;
            _documents.DocumentChanged -= OnDocumentChanged;
            _snapshot = null; _frozen = null; _analysis = null; _previousAnalysis = null; _queries.Clear(); _queryOwners.Clear(); _querySources.Clear(); _programs = new Dictionary<string, ShaderEffectDescriptor?>(); Revision++; Changed = null;
            _variants = new Dictionary<string, ImageVariantsDefinition?>();
        }
    }
}
