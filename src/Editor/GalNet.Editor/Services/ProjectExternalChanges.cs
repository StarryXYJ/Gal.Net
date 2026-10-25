using Avalonia.Threading;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public enum ExternalProjectState { Ready, Reloaded, Publishing, LocalChanges, Invalid, ContractsChanged }

/// <summary>Source-independent disk reconciliation. Watcher events are hints, never author content.</summary>
public sealed class ProjectExternalChanges(IProjectService projects, IProjectContentSession content, IEntryCatalog catalog,
    EditorHistories histories, IVariableDefinitionService variables) : IProjectEditLifetime, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _timer;
    private IReadOnlyDictionary<string, byte[]>? _observedFiles;
    private string? _observedContracts;
    private string? _root;
    private bool _disposed;
    private bool _suspended;
    private int _requested = 1;
    private long _lastNotification;
    private DateTime _lastScan = DateTime.MinValue;
    private Task? _running;
    public ExternalProjectState State { get; private set; }
    public string Details { get; private set; } = "";
    public bool HasIssue => State is ExternalProjectState.LocalChanges or ExternalProjectState.Invalid or ExternalProjectState.ContractsChanged or ExternalProjectState.Publishing;
    public bool CanReload => State is not (ExternalProjectState.ContractsChanged or ExternalProjectState.Publishing);
    public event Action? Changed;

    public void Start()
    {
        if (_disposed || _watcher is not null) return;
        _root = projects.Current?.RootPath ?? throw new InvalidOperationException("No project is open.");
        _watcher = new(_root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        _watcher.Changed += OnFile; _watcher.Created += OnFile; _watcher.Deleted += OnFile; _watcher.Renamed += OnFile;
        _watcher.Error += (_, _) => Interlocked.Exchange(ref _requested, 1);
        _watcher.EnableRaisingEvents = true;
        content.Changed += RequestScan;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += OnTick; _timer.Start();
    }

    private void RequestScan() { if (HasIssue) Interlocked.Exchange(ref _requested, 1); }
    private void OnFile(object sender, FileSystemEventArgs args)
    {
        var path = Path.GetRelativePath(_root!, args.FullPath).Replace('\\', '/');
        var first = path.Split('/')[0];
        if (new[] { ".git", "Output", "Temp", "bin", "obj", ".vs", ".idea" }.Contains(first, StringComparer.OrdinalIgnoreCase)) return;
        if (first.Equals(".galnet", StringComparison.OrdinalIgnoreCase) && !path.StartsWith(".galnet/editor/transactions/", StringComparison.OrdinalIgnoreCase)) return;
        Interlocked.Exchange(ref _lastNotification, Environment.TickCount64);
        Interlocked.Exchange(ref _requested, 1);
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_disposed || _suspended || _running is { IsCompleted: false } || content.IsPublishing || histories.Content.IsBusy) return;
        if (Environment.TickCount64 - Interlocked.Read(ref _lastNotification) < 600) return;
        if (Interlocked.Exchange(ref _requested, 0) == 0 && DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(5)) return;
        _running = CheckNowAsync();
        await _running;
    }

    public async Task<ExternalReloadResult> CheckNowAsync(bool discardLocalChanges = false)
    {
        if (_disposed || _suspended || projects.Current is null) return ExternalReloadResult.Unavailable;
        var acquired = false;
        try
        {
            await _gate.WaitAsync(_lifetime.Token); acquired = true;
            if (_disposed || _suspended || content.IsPublishing || histories.Content.IsBusy) return ExternalReloadResult.Retry;
            _root ??= projects.Current.RootPath;
            _lastScan = DateTime.UtcNow;
            var tracked = content.FileDocuments.Values.Select(file => new EditorFileDocument { Path = file.Path, ManagedContent = file.ManagedContent }).ToArray();
            var read = await Task.Run(() =>
            {
                if (ExternalProjectSnapshotReader.HasPendingTransaction(_root)) return (Files: (Dictionary<string, byte[]>?)null, Contracts: "", Snapshot: (ExternalProjectSnapshot?)null);
                var files = ExternalProjectSnapshotReader.ReadFiles(_root, tracked);
                var contracts = ExternalProjectSnapshotReader.ContractsFingerprint(_root);
                var unchanged = !discardLocalChanges && _observedFiles is not null && contracts == _observedContracts && ExternalProjectSnapshotReader.Same(files, _observedFiles);
                return (Files: files, Contracts: contracts, Snapshot: unchanged ? null : ExternalProjectSnapshotReader.Read(_root, tracked, catalog));
            }, _lifetime.Token);
            if (_disposed || _suspended) return ExternalReloadResult.Unavailable;
            _lastScan = DateTime.UtcNow;
            if (read.Files is null) { SetState(ExternalProjectState.Publishing); Interlocked.Exchange(ref _requested, 1); return ExternalReloadResult.Retry; }
            if (read.Snapshot is null) { if (HasIssue) SetState(ExternalProjectState.Ready); return ExternalReloadResult.Unchanged; }
            var result = await content.ReloadExternalAsync(read.Snapshot, discardLocalChanges, _lifetime.Token);
            if (result is ExternalReloadResult.Reloaded or ExternalReloadResult.Unchanged)
            {
                _observedFiles = read.Snapshot.Files; _observedContracts = read.Contracts;
                SetState(result == ExternalReloadResult.Reloaded ? ExternalProjectState.Reloaded : ExternalProjectState.Ready);
                if (result == ExternalReloadResult.Reloaded) variables.ResetFromDocument();
            }
            else if (result == ExternalReloadResult.LocalChanges) SetState(ExternalProjectState.LocalChanges);
            else if (result == ExternalReloadResult.Retry) Interlocked.Exchange(ref _requested, 1);
            return result;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return ExternalReloadResult.Unavailable; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            if (!_disposed && !_suspended) SetState(error is EditorOperationException { Code: "project.externalContracts" } ? ExternalProjectState.ContractsChanged : ExternalProjectState.Invalid, error.Message);
            return ExternalReloadResult.Retry;
        }
        finally { if (acquired) _gate.Release(); }
    }

    private void SetState(ExternalProjectState state, string details = "")
    {
        if (State == state && Details == details) return;
        State = state; Details = details; Changed?.Invoke();
    }

    public async Task SuspendAsync()
    {
        _suspended = true; _timer?.Stop();
        await _gate.WaitAsync(); _gate.Release();
    }
    public void ResumeEditing() { if (_disposed) return; _suspended = false; _timer?.Start(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _suspended = true;
        _timer?.Stop(); if (_timer is not null) _timer.Tick -= OnTick;
        _watcher?.Dispose(); content.Changed -= RequestScan;
        _lifetime.Cancel();
        // An in-flight read owns these until its continuation has observed cancellation.
        GC.SuppressFinalize(this);
    }
}
