using Avalonia.Threading;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Shared source names. Metadata IO is project-owned and bounded; reads never block the UI.</summary>
internal sealed class SceneSourceDescriptions(IEditorDocumentService documents, IAssetManager assets, IEditorLocalizationService text) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _loading = new(StringComparer.Ordinal);
    private long _epoch;
    public event Action? Changed;
    public string Resource(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string? path; lock (_gate) path = _paths.GetValueOrDefault(id);
        if (path is null) _ = PrepareAsync(id);
        return !string.IsNullOrEmpty(path) ? Path.GetFileName(path) : Guid.TryParse(id, out _) ? text["Scene.Unresolved"] : id;
    }
    public string Origin(SceneTarget target)
    {
        var group = documents.CurrentDocument.Nodes.FirstOrDefault(node => node.Id == target.Locator.GroupId);
        var entry = documents.Groups.GetValueOrDefault(target.Locator.GroupId)?.Entries.FirstOrDefault(item => item.StableId == target.Locator.EntryId);
        return (group?.Name ?? "") + " / " + entry?.Id;
    }
    public Task PrepareAsync(string id)
    {
        lock (_gate)
        {
            if (_disposed || _paths.ContainsKey(id)) return Task.CompletedTask;
            if (_loading.TryGetValue(id, out var pending)) return pending;
            if (_loading.Count >= 64) return Task.CompletedTask;
            var epoch = _epoch;
            var token = _lifetime.Token;
            return _loading[id] = Task.Run(async () =>
            {
                string path = "";
                try { path = (await assets.GetFileAsync(id, token))?.Path ?? ""; }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception failure) { Serilog.Log.Debug(failure, "Cannot describe scene resource {Resource}", id); }
                lock (_gate)
                {
                    if (_disposed || epoch != _epoch) return;
                    _loading.Remove(id);
                    if (_paths.Count >= 512) _paths.Remove(_paths.Keys.First());
                    _paths[id] = path;
                }
                if (path.Length != 0) Dispatcher.UIThread.Post(() => { if (!_disposed && epoch == _epoch) Changed?.Invoke(); });
            });
        }
    }
    public void Invalidate()
    { lock (_gate) { if (_disposed) return; _epoch++; _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new(); _paths.Clear(); _loading.Clear(); } }
    public void Dispose()
    { lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); _paths.Clear(); _loading.Clear(); Changed = null; } }
}
