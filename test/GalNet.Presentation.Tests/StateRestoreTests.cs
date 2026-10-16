using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class StateRestoreTests
{
    public sealed class CustomState { public string Id { get; init; } = ""; public int Value { get; init; } }
    [Test]
    public async Task CustomModuleRestoresInDependencyOrderWithoutAnyBuiltinDispatch()
    {
        var calls = new List<string>();
        using var registration = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
        {
            registry.Restore("dependent", _ => { calls.Add("dependent"); return Task.CompletedTask; }, "custom");
            registry.State<CustomState>("custom", value => value.Id, context =>
            {
                Assert.That(context.Runtime.State.Table<CustomState>().Get("one").Value, Is.EqualTo(7));
                calls.Add("custom"); return Task.CompletedTask;
            });
        }));
        using var view = registration.BuildView();
        var source = new GameRuntime(null, stateCatalog: view.StateCatalog);
        source.State.Table<CustomState>().Add(new CustomState { Id = "one", Value = 7 });
        var restored = new GameRuntime(null, stateCatalog: view.StateCatalog); restored.RestoreFrom(source.CreateSnapshot());
        await using var restorer = new StateRestoreSession(view);
        await restorer.RestoreAsync(restored);
        Assert.That(calls, Is.EqualTo(new[] { "custom", "dependent" }));
    }
    [Test]
    public void CyclicRestorationFailsDuringComposition()
    {
        using var registration = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
        {
            registry.Restore("a", _ => Task.CompletedTask, "b");
            registry.Restore("b", _ => Task.CompletedTask, "a");
        }));
        Assert.That(registration.BuildView, Throws.InvalidOperationException.With.Message.Contains("cycle"));
    }
    [Test]
    public void MissingDependencyAndDuplicateRestorersAreRejected()
    {
        using var missing = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
            registry.Restore("custom", _ => Task.CompletedTask, "missing")));
        Assert.That(missing.BuildView, Throws.InvalidOperationException.With.Message.Contains("missing dependency"));
        using var duplicate = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
        {
            registry.Restore("custom", _ => Task.CompletedTask);
            registry.Restore("custom", _ => Task.CompletedTask);
        }));
        Assert.That(duplicate.BuildView, Throws.InvalidOperationException.With.Message.Contains("Duplicate state restorer"));
    }
    [Test]
    public void PartialRestorationOwnsAndDrainsEveryCleanupDespiteAnotherCleanupFailure()
    {
        var order = new List<int>();
        using var registration = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
            registry.State<CustomState>("custom", value => value.Id, context =>
            {
                context.Own(() => { order.Add(1); return Task.CompletedTask; });
                context.Own(() => { order.Add(2); throw new IOException("cleanup"); });
                throw new IOException("restore");
            })));
        using var view = registration.BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var restorer = new StateRestoreSession(view);
        Assert.ThrowsAsync<IOException>(() => restorer.RestoreAsync(runtime));
        var first = restorer.DisposeAsync().AsTask();
        Assert.ThrowsAsync<AggregateException>(() => first);
        Assert.That(order, Is.EqualTo(new[] { 2, 1 }));
        Assert.That(restorer.DisposeAsync().AsTask(), Is.SameAs(first));
    }
    [Test]
    public async Task CleanupFailureIsReportedOnceAndDoesNotRepeatReleasedResourcesOnRetry()
    {
        var cleanups = 0;
        using var registration = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
            registry.State<CustomState>("custom", value => value.Id, context =>
            {
                context.Own(() => { if (++cleanups == 1) throw new IOException("cleanup"); return Task.CompletedTask; });
                return Task.CompletedTask;
            })));
        using var view = registration.BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        await using var restorer = new StateRestoreSession(view);
        await restorer.RestoreAsync(runtime);
        Assert.ThrowsAsync<AggregateException>(() => restorer.SuspendAsync());
        await restorer.RestoreAsync(runtime);
        await restorer.SuspendAsync();
        Assert.That(cleanups, Is.EqualTo(2));
    }

    [Test]
    public async Task FailedRestorationPreventsAdvanceAndInvalidStateDoesNotCancelTheCurrentRun()
    {
        using var registration = new GameRegistration().RegisterImplementation(new PrimitiveModule(registry =>
            registry.State<CustomState>("custom", value => value.Id, _ => throw new IOException("restore failed"))));
        using var view = registration.BuildView();
        var graph = new Graph { RootNodeId = "main", Nodes = [new Group { Id = "main" }] };
        await using var engine = new GameEngine(graph, view);
        var saved = engine.CreateSaveData();
        var broken = engine.CreateSaveData(); broken.States.Clear();
        var oldState = engine.Runtime.State;
        Assert.Throws<InvalidDataException>(() => engine.RestoreFrom(broken));
        Assert.That(engine.Runtime.State, Is.SameAs(oldState));
        engine.RestoreFrom(saved);
        Assert.That(await engine.AdvanceAsync(), Is.True);
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
        await using var restorer = new StateRestoreSession(view);
        Assert.ThrowsAsync<IOException>(() => engine.SynchronizePresentationAsync(restorer.RestoreAsync));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Faulted));
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => engine.AdvanceAsync());
        Assert.That(error!.InnerException, Is.TypeOf<IOException>());
    }
}
