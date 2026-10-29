using GalNet.Core.Runtime;
using GalNet.GameHost;

namespace GalNet.GameHost.Tests;

public sealed class GameSessionTests
{
    [Test]
    public async Task InvalidReplacementSaveDoesNotStopOrReleaseCurrentRun()
    {
        var run = new Run { FailValidate = true };
        var creations = 0;
        await using var session = new GameSession(_ => { creations++; return Task.FromResult<IGameRun>(run); });
        await session.PrepareAsync(null);
        Assert.ThrowsAsync<InvalidDataException>(() => session.PrepareAsync(new GameSnapshot()));
        Assert.That(run.Calls, Is.EqualTo(new[] { "prepare", "validate" }));
        Assert.That(creations, Is.EqualTo(1));
        await session.BeginPreparedAsync();
        Assert.That(run.Calls.Last(), Is.EqualTo("start"));
    }

    [Test]
    public async Task ReplacementWaitsForSnapshotWriteBeforeStoppingOldRun()
    {
        var runs = new List<Run>();
        await using var session = new GameSession(_ =>
        {
            var run = new Run(); runs.Add(run); return Task.FromResult<IGameRun>(run);
        });
        await session.PrepareAsync(null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = session.WithSnapshotAsync(async (_, _) => { entered.SetResult(); await release.Task; return true; });
        await entered.Task;
        var replace = session.PrepareAsync(null);
        Assert.That(runs[0].Calls, Is.EqualTo(new[] { "prepare" }));
        release.SetResult();
        await save; await replace;
        Assert.That(runs[0].Calls, Is.EqualTo(new[] { "prepare", "stop", "dispose" }));
        Assert.That(runs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FailedPreparationReleasesRunAndAllowsRetry()
    {
        var failed = new Run { FailPrepare = true };
        var next = new Run();
        var attempts = 0;
        await using var session = new GameSession(_ => Task.FromResult<IGameRun>(attempts++ == 0 ? failed : next));
        Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareAsync(null));
        Assert.That(failed.Calls, Is.EqualTo(new[] { "prepare", "dispose" }));
        await session.PrepareAsync(null);
        await session.BeginPreparedAsync();
        Assert.That(next.Calls, Is.EqualTo(new[] { "prepare", "start" }));
    }

    [Test]
    public async Task StopFailureStillDisposesRunAndRepeatedDisposalSharesFailure()
    {
        var run = new Run { FailStop = true };
        var session = new GameSession(_ => Task.FromResult<IGameRun>(run));
        await session.PrepareAsync(null);
        var first = session.DisposeAsync().AsTask();
        var second = session.DisposeAsync().AsTask();
        Assert.That(second, Is.SameAs(first));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
        Assert.That(run.Calls, Is.EqualTo(new[] { "prepare", "stop", "dispose" }));
        Assert.ThrowsAsync<ObjectDisposedException>(() => session.PrepareAsync(null));
    }

    private sealed class Run : IGameRun
    {
        public List<string> Calls { get; } = [];
        public bool FailPrepare { get; init; }
        public bool FailStop { get; init; }
        public bool FailValidate { get; init; }
        public Task ValidateSnapshotAsync(GameSnapshot snapshot, CancellationToken cancellationToken)
        {
            Calls.Add("validate");
            return FailValidate ? Task.FromException(new InvalidDataException("bad save")) : Task.CompletedTask;
        }
        public Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
        {
            Calls.Add("prepare");
            return FailPrepare ? Task.FromException(new InvalidOperationException("prepare")) : Task.CompletedTask;
        }
        public Task StartPreparedAsync(CancellationToken cancellationToken) { Calls.Add("start"); return Task.CompletedTask; }
        public Task StopAsync()
        {
            Calls.Add("stop");
            return FailStop ? Task.FromException(new InvalidOperationException("stop")) : Task.CompletedTask;
        }
        public GameSnapshot CreateSaveData() => new();
        public ValueTask DisposeAsync() { Calls.Add("dispose"); return ValueTask.CompletedTask; }
    }
}
