using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectDeleteServiceTests
{
    private string _root = null!;
    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-recycle-unit-" + Guid.NewGuid());
        await EditorProjectCreator.CreateAsync(new(_root));
    }
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task DryRun_IdentifiesContractlessProject_WithoutCallingHostCallbacks()
    {
        var bin = new TestBin();
        var result = await new ProjectDeleteService(bin).DeleteAsync(_root, true,
            _ => throw new Exception("must not close"), _ => throw new Exception("must not remove recent"));
        Assert.That(result.Success, Is.True);
        Assert.That(((ProjectDeleteReport)result.Data!).Recycled, Is.False);
        Assert.That(bin.Calls, Is.Zero);
        Assert.That(Directory.Exists(_root), Is.True);
    }

    [Test]
    public async Task Shutdown_Recycle_Recent_AreOrdered_AndFailureDoesNotForgetProject()
    {
        var calls = new List<string>();
        var bin = new TestBin { Callback = _ => { calls.Add("recycle"); return Task.CompletedTask; } };
        var service = new ProjectDeleteService(bin);
        var result = await service.DeleteAsync(_root, beforeRecycle: _ => { calls.Add("close"); return Task.CompletedTask; }, afterRecycle: _ => calls.Add("recent"));
        Assert.That(result.Success, Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "close", "recycle", "recent" }));
        calls.Clear();
        bin.Callback = _ => throw new IOException("recycle failed");
        result = await service.DeleteAsync(_root, beforeRecycle: _ => { calls.Add("close"); return Task.CompletedTask; }, afterRecycle: _ => calls.Add("recent"));
        Assert.That(result.Success, Is.False);
        Assert.That(calls, Is.EqualTo(new[] { "close" }));
        Assert.That(Directory.Exists(_root), Is.True);
    }

    [Test]
    public async Task ShutdownFailure_UnsupportedHost_AndCancellation_NeverRecycle()
    {
        var bin = new TestBin();
        var service = new ProjectDeleteService(bin);
        Assert.That((await service.DeleteAsync(_root, beforeRecycle: _ => throw new Exception("preview still active"))).Success, Is.False);
        bin.IsSupported = false;
        Assert.That((await service.DeleteAsync(_root)).Success, Is.False);
        Assert.ThrowsAsync<OperationCanceledException>(() => service.DeleteAsync(_root, cancellationToken: new(true)));
        Assert.That(bin.Calls, Is.Zero);
        Assert.That(Directory.Exists(_root), Is.True);
    }

    [TestCase("missing")]
    [TestCase("old-format")]
    [TestCase("root")]
    public async Task UnrecognizedTargets_AreRejectedBeforeCallingRecycle(string kind)
    {
        var bin = new TestBin();
        var path = _root;
        if (kind == "missing") File.Delete(Path.Combine(_root, "settings.json"));
        else if (kind == "old-format") File.WriteAllText(Path.Combine(_root, "Graph/graph.json"), "{\"version\":1}");
        else path = Path.GetPathRoot(_root)!;
        Assert.That((await new ProjectDeleteService(bin).DeleteAsync(path)).Success, Is.False);
        Assert.That(bin.Calls, Is.Zero);
        Assert.That(Directory.Exists(_root), Is.True);
    }

    [Test]
    public async Task RecentUpdateFailure_ReportsThatRecyclingAlreadySucceeded()
    {
        var result = await new ProjectDeleteService(new TestBin()).DeleteAsync(_root, afterRecycle: _ => throw new IOException("settings locked"));
        Assert.That(result.Success, Is.False);
        Assert.That(((ProjectDeleteReport)result.Data!).Recycled, Is.True);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("project.recentUpdateFailed"));
    }

    private sealed class TestBin : IProjectRecycleBin
    {
        public bool IsSupported { get; set; } = true;
        public int Calls { get; private set; }
        public Func<string, Task>? Callback { get; set; }
        public Task RecycleAsync(string path, CancellationToken cancellationToken = default) { Calls++; return Callback?.Invoke(path) ?? Task.CompletedTask; }
    }
}
