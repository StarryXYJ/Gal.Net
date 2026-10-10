using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class CompositeGameViewTests
{
    [Test]
    public async Task AsyncDisposalThroughGameViewInterfaceUsesModuleAsyncCleanup()
    {
        var module = new AsyncDisposableModule();
        await using (IGameView view = new GameRegistration().RegisterImplementation(module).BuildView())
            Assert.That(module.CleanedUp, Is.False);
        Assert.That(module.CleanedUp, Is.True);
    }

    private sealed class AsyncDisposableModule() : PrimitiveModule([])
    {
        public bool CleanedUp { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            CleanedUp = true;
            await base.DisposeAsync();
        }
    }
    [Test]
    public void SchemaAndImplementationCanBeRegisteredIndependently()
    {
        PrimitiveCreateContext? received = null;
        var parameters = new DynamicParameterTable(
        [
            new DynamicParameterDescriptor("visible", typeof(bool), isRequired: true),
            new DynamicParameterDescriptor("opacity", typeof(float), defaultValue: JsonSerializer.SerializeToElement(1f))
        ]);
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("layer", [new DefaultPrimitiveEntryBase("layer.show", parameters)]));
        Assert.That(registration.BuildCatalog().Get("layer.show").DynamicParameters, Is.SameAs(parameters));
        registration.RegisterInstruction("layer.show", context =>
        {
            received = context;
            return new ImmediatePrimitiveInstance(batchId: context.BatchId);
        });
        using var view = registration.BuildView();
        var instance = view.Dispatch(Entry("layer.show", new { visible = true }, "opening"), new GameRuntime(null), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(instance.BatchId, Is.EqualTo("opening"));
            Assert.That(received!.Parameters, Is.SameAs(parameters));
            Assert.That(received.Arguments.GetProperty("opacity").GetSingle(), Is.EqualTo(1f));
        });
    }

    [Test]
    public void ModulesWithTheSamePrefixAndNameCanContributeDifferentInstructions()
    {
        using var registration = new GameRegistration();
        registration.RegisterSchema(Schema("custom", "custom.first"));
        registration.RegisterSchema(Schema("custom", "custom.second"));
        registration.RegisterInstruction("custom.first", context => new ImmediatePrimitiveInstance(batchId: context.BatchId));
        registration.RegisterInstruction("custom.second", context => new ImmediatePrimitiveInstance(batchId: context.BatchId));
        using var view = registration.BuildView();
        Assert.That(view.CanExecute("custom.first") && view.CanExecute("custom.second"), Is.True);
    }

    [Test]
    public void DuplicateFullInstructionIdsAndMissingSchemasAreRejected()
    {
        using var duplicate = new GameRegistration();
        duplicate.RegisterSchema(Schema("test", "test.run"));
        duplicate.RegisterInstruction("test.run", _ => new ImmediatePrimitiveInstance());
        duplicate.RegisterInstruction("test.run", _ => new ImmediatePrimitiveInstance());
        Assert.That(duplicate.BuildView, Throws.InvalidOperationException.With.Message.Contains("already registered"));

        using var missing = new GameRegistration();
        missing.RegisterInstruction("test.run", _ => new ImmediatePrimitiveInstance());
        Assert.That(missing.BuildView, Throws.InvalidOperationException.With.Message.Contains("no primitive schema"));
    }

    [Test]
    public void MissingImplementationAndWrongBatchAreExplicitErrors()
    {
        using var registration = new GameRegistration();
        registration.RegisterSchema(Schema("test", "test.run", "test.missing"));
        registration.RegisterInstruction("test.run", _ => new ImmediatePrimitiveInstance(batchId: "wrong"));
        using var view = registration.BuildView();
        Assert.That(() => view.Dispatch(Entry("test.missing", new { }), new GameRuntime(null), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("no registered implementation"));
        Assert.That(() => view.Dispatch(Entry("test.run", new { }, "expected"), new GameRuntime(null), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("BatchId"));
    }

    [Test]
    public void DisposalIsReverseOrderedIdempotentAndIncludesEveryModuleDespiteErrors()
    {
        var order = new List<int>();
        using var registration = new GameRegistration();
        registration.RegisterImplementation(new DisposableModule(1, order));
        registration.RegisterImplementation(new DisposableModule(2, order, fail: true));
        var view = registration.BuildView();
        Assert.That(() => registration.RegisterSchema(Schema("test", "test.run")), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(view.Dispose, Throws.TypeOf<AggregateException>());
        Assert.That(order, Is.EqualTo(new[] { 2, 1 }));
        Assert.DoesNotThrow(view.Dispose);
    }

    private static IEntryModule Schema(string module, params string[] types) =>
        new DefaultEntryModule(module, types.Select(type => new DefaultPrimitiveEntryBase(type, DynamicParameterTable.Empty)));

    private static PrimitiveEntry Entry(string type, object arguments, string? batch = null) =>
        new(type, JsonSerializer.SerializeToElement(arguments), batch);

    private sealed class DisposableModule(int id, List<int> order, bool fail = false) : PrimitiveModule([])
    {
        public override void Dispose()
        {
            order.Add(id);
            if (fail) throw new IOException("expected");
            base.Dispose();
        }
    }
}
