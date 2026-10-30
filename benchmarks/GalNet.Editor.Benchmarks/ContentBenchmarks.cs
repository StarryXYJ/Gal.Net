using System.Text.Json;
using BenchmarkDotNet.Attributes;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Benchmarks;

/// <summary>Author-content operations on the calling thread; excludes UI rendering and setup.</summary>
[MemoryDiagnoser]
public class ContentBenchmarks : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string ScratchRoot = Path.Combine(Path.GetTempPath(), "GalNet.EditorBenchmarks");
    private readonly string _root = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));
    private readonly JsonElement _original = JsonSerializer.SerializeToElement(1f);
    private readonly JsonElement _changed = JsonSerializer.SerializeToElement(2f);
    private readonly IOException _failure = new("Benchmark recovery request.");
    private ProjectContentSession _session = null!;
    private GalProject _project = null!;
    private EditorDocumentService _documents = null!;
    private EditorProjectDocument _snapshot = null!;
    private ProjectRecoveryStore _unchangedStore = null!;
    private ProjectRecoveryStore _changedStore = null!;
    private EditorEntryData _lastEntry = null!;
    private byte[] _lastText = [];
    private int _revision;
    private Task _scheduledRecovery = Task.CompletedTask;

    [IterationSetup(Target = nameof(RecoverySchedule))]
    public void PrepareRecoverySchedule() => _documents.CurrentDocument.Name = (++_revision).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // One request per iteration so the caller's synchronous work is measured without queue buildup.
    [Benchmark]
    public void RecoverySchedule() => _scheduledRecovery = _session.CaptureRecoveryAsync();

    [IterationCleanup(Target = nameof(RecoverySchedule))]
    public void CompleteRecoverySchedule()
    {
        _scheduledRecovery.GetAwaiter().GetResult();
        if (_session.LastRecoveryError is { } failure) throw new InvalidDataException("Scheduled recovery failed.", failure);
    }

    [Params(1000, 10000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        EditorProjectCreator.CreateAsync(new(_root, "Benchmark")).GetAwaiter().GetResult();
        var repository = new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var loaded = repository.Load(_root);
        loaded.Document.Nodes.Clear();
        loaded.Document.Edges.Clear();
        loaded.GroupEntries.Clear();
        loaded.Document.RootNodeId = "group-00";
        for (var group = 0; group < 20; group++)
        {
            var id = $"group-{group:D2}";
            loaded.Document.Nodes.Add(new() { Id = id, Name = id, Type = "Group", File = $"groups/{id}.rawgalgroup" });
            var entries = new List<EditorEntryData>();
            for (var index = 0; index < EntryCount / 20; index++)
                entries.Add(new()
                {
                    StableId = $"{id}-{index}", Id = index + 1, Type = BuiltinEntryIds.Wait,
                    Parameters = new()
                    {
                        ["duration"] = _original,
                        ["future"] = JsonSerializer.SerializeToElement(new { text = "作者内容与未知字段", values = new[] { index, group } })
                    }
                });
            loaded.GroupEntries.Add(id, entries);
        }
        _lastEntry = loaded.GroupEntries.Last().Value[^1];
        _documents = new();
        _documents.Load(loaded);
        _project = new("benchmark", "Benchmark", _root, DirectProjectPersistence.LoadSettings(_root), new(), new EmptyScope());
        var options = new ProjectSessionOptions { RecoveryInterval = TimeSpan.FromDays(1) };
        _session = new(_documents, repository, options);
        _session.Open(_project);
        // TrackFile establishes each file's saved baseline without writing author files.
        for (var index = 0; index < 8; index++)
        {
            var text = new byte[EntryCount * 1024 / 8];
            Array.Fill(text, (byte)'a');
            _lastText = _session.TrackFile($"notes/{index}.txt", text).Data;
        }
        _snapshot = _session.CaptureWorking();
        Directory.CreateDirectory(Path.Combine(_root, "unchanged"));
        Directory.CreateDirectory(Path.Combine(_root, "changed"));
        _unchangedStore = new(Path.Combine(_root, "unchanged"), options);
        _changedStore = new(Path.Combine(_root, "changed"), options);
        _unchangedStore.Capture(_snapshot);
    }

    [Benchmark]
    public bool DirtyClean() => _session.IsDirty;

    [Benchmark]
    public bool DirtyLastEntry()
    {
        _lastEntry.Parameters["duration"] = _changed;
        try { return _session.IsDirty; }
        finally { _lastEntry.Parameters["duration"] = _original; }
    }

    [Benchmark]
    public bool DirtyLastTextByte()
    {
        _lastText[^1] ^= 1;
        try { return _session.IsDirty; }
        finally { _lastText[^1] ^= 1; }
    }

    [Benchmark]
    public EditorProjectDocument CaptureWorking() => _session.CaptureWorking();

    [Benchmark]
    public byte[] SerializeSnapshot() => JsonSerializer.SerializeToUtf8Bytes(_snapshot, Json);

    [Benchmark]
    public string? RecoveryUnchanged() => _unchangedStore.Capture(_snapshot);

    [Benchmark]
    public string? RecoveryChanged()
    {
        _snapshot.Graph.Name = (++_revision).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _changedStore.Capture(_snapshot);
    }

    [Benchmark]
    public void RecoveryThroughSession()
    {
        _documents.CurrentDocument.Name = (++_revision).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _session.ReportError(_failure);
    }

    public void Verify()
    {
        if (DirtyClean() || !DirtyLastEntry() || !DirtyLastTextByte() || DirtyClean())
            throw new InvalidDataException("Dirty comparison missed a late direct mutation or failed to return to clean.");
        var detached = CaptureWorking();
        _lastEntry.Parameters["duration"] = _changed;
        _lastText[^1] ^= 1;
        if (detached.GroupEntries.Last().Value[^1].Parameters["duration"].GetSingle() != 1f ||
            detached.Files.Last().Value.Data[^1] != (byte)'a')
            throw new InvalidDataException("Captured content shares mutable state with the live document.");
        _lastEntry.Parameters["duration"] = _original;
        _lastText[^1] ^= 1;
        if (RecoveryUnchanged() is not null) throw new InvalidDataException("Unchanged recovery was written again.");
        var path = RecoveryChanged() ?? throw new InvalidDataException("Changed recovery was skipped.");
        var restored = ProjectRecoveryStore.Read(path);
        if (!JsonSerializer.SerializeToUtf8Bytes(restored, Json).AsSpan().SequenceEqual(SerializeSnapshot()))
            throw new InvalidDataException("Recovery roundtrip changed author content.");
        RecoveryThroughSession();
        if (_session.LastRecoveryError is not null) throw new InvalidDataException("Session recovery failed.", _session.LastRecoveryError);
        var latest = Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery"), "*.json").Single();
        if (ProjectRecoveryStore.Read(latest).Graph.Name != _documents.CurrentDocument.Name)
            throw new InvalidDataException("Session recovery did not retain the current revision.");
        PrepareRecoverySchedule();
        RecoverySchedule();
        CompleteRecoverySchedule();
        latest = Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery"), "*.json")
            .OrderDescending(StringComparer.Ordinal).First();
        if (ProjectRecoveryStore.Read(latest).Graph.Name != _documents.CurrentDocument.Name)
            throw new InvalidDataException("Scheduled recovery did not retain the current revision.");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _session?.Dispose();
        _project?.Dispose();
        if (Path.GetDirectoryName(Path.GetFullPath(_root)) != Path.GetFullPath(ScratchRoot))
            throw new InvalidOperationException("Benchmark scratch path escaped its root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class EmptyScope : IServiceScope
    {
        public IServiceProvider ServiceProvider => EmptyProvider.Instance;
        public void Dispose() { }
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public static EmptyProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
