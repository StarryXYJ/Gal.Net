using System.Collections.Concurrent;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class AssetDiagnosticTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Missing_resource_reports_the_requested_identity_once(bool byPath)
    {
        using var manager = new AssetManager();
        var reports = Observe(manager);
        var handle = byPath ? await manager.AcquireByPathAsync<byte[]>("folder/missing.bin") : await manager.AcquireAsync<byte[]>("missing");
        Assert.That(handle, Is.Null);
        var report = reports.Single();
        Assert.That(report.Kind, Is.EqualTo(AssetFailureKind.NotFound));
        Assert.That(report.Stage, Is.EqualTo(AssetLoadStage.Lookup));
        Assert.That(report.AssetId, Is.EqualTo(byPath ? null : "missing"));
        Assert.That(report.Path, Is.EqualTo(byPath ? "folder/missing.bin" : null));
        Assert.That(report.TargetType, Is.EqualTo(typeof(byte[])));
        Assert.That(report.Exception, Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Provider_failure_retains_exception_and_does_not_add_a_missing_report(bool invalidData, bool fallback)
    {
        Exception error = invalidData ? new InvalidDataException("invalid manifest") : new IOException("unavailable");
        using var manager = new AssetManager([new Provider("broken", () => throw error)]);
        if (fallback) manager.RegisterProvider(new Provider("fallback", () => new View(new TestFile())));
        var reports = Observe(manager);
        using var handle = await manager.AcquireAsync<byte[]>("asset");
        Assert.That(handle is not null, Is.EqualTo(fallback));
        var report = reports.Single();
        Assert.That(report.Kind, Is.EqualTo(invalidData ? AssetFailureKind.InvalidData : AssetFailureKind.ProviderFailure));
        Assert.That(report.Provider, Is.EqualTo("broken"));
        Assert.That(report.Exception, Is.SameAs(error));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Actual_pak_integrity_failure_is_observable_for_id_and_path(bool byPath)
    {
        var bytes = Archive.Serialize("assets", [new TestFile()], BuiltinResourceTypes.CreateCatalog(), CompressionMode.None);
        bytes[^1] ^= 1;
        using var manager = new AssetManager([new PakStreamProvider(new Dictionary<string, Func<Stream>>
        { ["assets"] = () => new MemoryStream(bytes, writable: false) }, BuiltinResourceTypes.CreateCatalog())]);
        var reports = Observe(manager);
        Assert.That(byPath ? await manager.AcquireByPathAsync<byte[]>("asset.bin") : await manager.AcquireAsync<byte[]>("asset"), Is.Null);
        var report = reports.Single();
        Assert.That(report.Kind, Is.EqualTo(AssetFailureKind.InvalidData));
        Assert.That(report.Provider, Is.EqualTo("PakStream"));
        Assert.That(report.Exception, Is.TypeOf<InvalidDataException>());
        Assert.That(report.Exception!.Message, Does.Contain("verification"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Deferred_read_failure_keeps_resolved_identity_and_provider(bool byPath)
    {
        var error = new IOException("read failed");
        using var manager = new AssetManager([new Provider("disk", () => new View(new TestFile(_ => throw error)))]);
        var reports = Observe(manager);
        Assert.That(byPath ? await manager.AcquireByPathAsync<byte[]>("asset.bin") : await manager.AcquireAsync<byte[]>("asset"), Is.Null);
        var report = reports.Single();
        Assert.That(report.Stage, Is.EqualTo(AssetLoadStage.Read));
        Assert.That(report.Kind, Is.EqualTo(AssetFailureKind.ReadFailure));
        Assert.That(report.AssetId, Is.EqualTo("asset"));
        Assert.That(report.Path, Is.EqualTo("asset.bin"));
        Assert.That(report.Provider, Is.EqualTo("disk"));
        Assert.That(report.Exception, Is.SameAs(error));
    }

    [TestCase("missing")]
    [TestCase("null")]
    [TestCase("throw")]
    public async Task Decoder_failures_are_distinct_and_preserve_the_original_exception(string mode)
    {
        var error = new InvalidDataException("bad pixels");
        using var manager = new AssetManager([new Provider("images", () => new View(new TestFile()))]);
        if (mode != "missing") manager.RegisterDecoder("data", new Decoder(_ => mode == "throw" ? throw error : ValueTask.FromResult<Decoded?>(null)));
        var reports = Observe(manager);
        Assert.That(await manager.AcquireAsync<Decoded>("asset"), Is.Null);
        var report = reports.Single();
        Assert.That(report.Stage, Is.EqualTo(AssetLoadStage.Decode));
        Assert.That(report.Kind, Is.EqualTo(mode == "missing" ? AssetFailureKind.DecoderMissing : AssetFailureKind.DecodeFailure));
        Assert.That(report.Exception, Is.SameAs(mode == "throw" ? error : null));
        Assert.That(report.Provider, Is.EqualTo("images"));
        Assert.That(report.TargetType, Is.EqualTo(typeof(Decoded)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Shared_failure_is_reported_once_and_can_be_retried_successfully(bool byPath)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new AssetManager([new Provider("images", () => new View(new TestFile()))]);
        manager.RegisterDecoder("data", new Decoder(async ct => { await release.Task.WaitAsync(ct); throw new IOException("decode failed"); }));
        var reports = Observe(manager);
        var waiters = Enumerable.Range(0, 12).Select(_ => byPath
            ? manager.AcquireByPathAsync<Decoded>("asset.bin") : manager.AcquireAsync<Decoded>("asset")).ToArray();
        release.SetResult();
        var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(results, Is.All.Null);
        Assert.That(reports, Has.Count.EqualTo(1));
        manager.RegisterDecoder("data", new Decoder(_ => ValueTask.FromResult<Decoded?>(new())));
        using var retried = await manager.AcquireAsync<Decoded>("asset");
        Assert.That(retried, Is.Not.Null);
        Assert.That(reports, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Canceling_a_waiter_does_not_report_or_cancel_the_other_waiter()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new AssetManager([new Provider("images", () => new View(new TestFile()))]);
        manager.RegisterDecoder("data", new Decoder(async ct => { await release.Task.WaitAsync(ct); return new(); }));
        var reports = Observe(manager);
        using var cancellation = new CancellationTokenSource();
        var first = manager.AcquireAsync<Decoded>("asset", cancellation.Token);
        var second = manager.AcquireAsync<Decoded>("asset");
        cancellation.Cancel();
        try { Assert.ThrowsAsync<TaskCanceledException>(async () => await first); }
        finally { release.TrySetResult(); }
        using var handle = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(handle, Is.Not.Null);
        Assert.That(reports, Is.Empty);
    }

    [TestCase(AssetLoadStage.Lookup)]
    [TestCase(AssetLoadStage.Read)]
    [TestCase(AssetLoadStage.Decode)]
    public void Cancellation_at_each_stage_is_not_a_failure_diagnostic(AssetLoadStage stage)
    {
        var error = new OperationCanceledException();
        using var manager = new AssetManager([new Provider("cancel", () => stage == AssetLoadStage.Lookup
            ? throw error : new View(new TestFile(stage == AssetLoadStage.Read ? _ => throw error : null)))]);
        manager.RegisterDecoder("data", new Decoder(_ => throw error));
        var reports = Observe(manager);
        Assert.ThrowsAsync<TaskCanceledException>(() => manager.AcquireAsync<Decoded>("asset"));
        Assert.That(reports, Is.Empty);
    }

    [Test]
    public async Task Throwing_observer_does_not_block_other_observers_or_shared_completion_and_runs_outside_lock()
    {
        using var manager = new AssetManager();
        var lockAvailable = false;
        manager.Diagnostic += _ =>
        {
            lockAvailable = Task.Run(() => manager.RegisterProvider(new Provider("added", () => new View(new TestFile())))).Wait(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("observer failed");
        };
        var reports = Observe(manager);
        Assert.That(await manager.AcquireAsync<byte[]>("missing").WaitAsync(TimeSpan.FromSeconds(7)), Is.Null);
        Assert.That(lockAvailable, Is.True);
        Assert.That(reports, Has.Count.EqualTo(1));
        using var handle = await manager.AcquireAsync<byte[]>("asset");
        Assert.That(handle, Is.Not.Null);
    }

    [Test]
    public async Task Enumeration_reports_failure_and_still_returns_later_provider_files()
    {
        var error = new InvalidDataException("bad entry");
        using var manager = new AssetManager([new Provider("broken", () => new View(new TestFile(), error)), new Provider("good", () => new View(new TestFile()))]);
        var reports = Observe(manager);
        var files = await manager.GetFilesAsync();
        Assert.That(files.Single().Id, Is.EqualTo("asset"));
        var report = reports.Single();
        Assert.That(report.Stage, Is.EqualTo(AssetLoadStage.Enumeration));
        Assert.That(report.AssetId, Is.EqualTo("asset"));
        Assert.That(report.Exception, Is.SameAs(error));
    }

    [Test]
    public async Task No_observer_keeps_failure_fallback_available()
    {
        using var manager = new AssetManager([new Provider("broken", () => throw new IOException("offline"))]);
        Assert.That(await manager.AcquireAsync<byte[]>("asset"), Is.Null);
        Assert.That(await manager.GetFilesAsync(), Is.Empty);
    }

    [Test]
    public async Task Cached_path_rechecks_a_missing_id_and_resolves_its_replacement()
    {
        IGameFile current = new TestFile();
        using var manager = new AssetManager([new Provider("changing", () => new View(current))]);
        using (var first = await manager.AcquireByPathAsync<byte[]>("asset.bin"))
            Assert.That(first!.AssetId, Is.EqualTo("asset"));
        current = new GameFile("replacement", "asset.bin", "data",
            new AssetMeta { Id = "replacement", Path = "asset.bin", TypeId = "data" }, [2]);
        using var replaced = await manager.AcquireByPathAsync<byte[]>("asset.bin");
        Assert.That(replaced!.AssetId, Is.EqualTo("replacement"));
        Assert.That(replaced.Value, Is.EqualTo(new byte[] { 2 }));
    }

    [Test]
    public async Task Cached_path_read_failure_is_attempted_once_and_later_request_can_recover()
    {
        var fail = false;
        var reads = 0;
        var file = new TestFile(_ =>
        {
            reads++;
            return fail ? throw new IOException("temporarily unavailable") : Task.FromResult(new byte[] { 1 });
        });
        using var manager = new AssetManager([new Provider("changing", () => new View(file))]);
        var reports = Observe(manager);
        using (var first = await manager.AcquireByPathAsync<byte[]>("asset.bin")) Assert.That(first, Is.Not.Null);
        fail = true;
        Assert.That(await manager.AcquireByPathAsync<byte[]>("asset.bin"), Is.Null);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(reports, Has.Count.EqualTo(1));
        fail = false;
        using var retried = await manager.AcquireByPathAsync<byte[]>("asset.bin");
        Assert.That(retried, Is.Not.Null);
        Assert.That(reads, Is.EqualTo(3));
    }

    private static ConcurrentQueue<AssetDiagnostic> Observe(AssetManager manager)
    {
        var reports = new ConcurrentQueue<AssetDiagnostic>();
        manager.Diagnostic += reports.Enqueue;
        return reports;
    }

    private sealed class Decoded;
    private sealed class Decoder(Func<CancellationToken, ValueTask<Decoded?>> decode) : IAssetDecoder<Decoded>
    {
        public ValueTask<Decoded?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default) => decode(ct);
    }
    private sealed class TestFile(Func<CancellationToken, Task<byte[]>>? read = null) : IGameFile
    {
        public string Id => "asset";
        public string Path => "asset.bin";
        public string TypeId => "data";
        public AssetMeta Metadata => new() { Id = Id, Path = Path, TypeId = TypeId };
        public long Length => 1;
        public string? Hash => null;
        public Stream OpenRead() => new MemoryStream(ReadAllBytes());
        public byte[] ReadAllBytes() => [1];
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => read?.Invoke(ct) ?? Task.FromResult(ReadAllBytes());
    }
    private sealed class Provider(string name, Func<IArchive> open) : IAssetProvider
    {
        public string Name => name;
        public bool Exists(string archiveName) => true;
        public IArchive OpenArchive(string archiveName) => open();
        public Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default) => Task.FromResult(open());
    }
    private sealed class View(IGameFile file, Exception? error = null) : IArchive
    {
        public string Name => "assets";
        public IEnumerable<string> AssetIds => [file.Id];
        public bool Contains(string assetId) => assetId == file.Id;
        public IGameFile? GetAsset(string assetId) => error is not null ? throw error : Contains(assetId) ? file : null;
        public IGameFile? GetAssetByPath(string path) => path == file.Path ? GetAsset(file.Id) : null;
        public void Dispose() { }
    }
}
