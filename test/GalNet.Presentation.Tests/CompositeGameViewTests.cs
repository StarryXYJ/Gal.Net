using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;
using GeneralTest.Presentation;

namespace GeneralTest.Runtime;

public class CompositeGameViewTests
{
    private static readonly int[] DisposalOrder = [2, 1];

    [Test]
    public void TypedFactoryUsesNormalizedDefaultsConvertersAndIndependentDtoInstances()
    {
        var received = new List<PlainFormDto>();
        PrimitiveCreateContext? receivedContext = null;
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("typed", [DefaultPrimitiveEntryBase.ForDto<PlainFormDto>("typed.run")]));
        registration.RegisterImplementation(new PrimitiveModule([
            PrimitiveImplementation.For<PlainFormDto>("typed.run", (context, options) =>
            {
                receivedContext = context;
                received.Add(options);
                return new ImmediatePrimitiveInstance(batchId: context.BatchId);
            })]));
        using var view = registration.BuildView();
        var runtime = new GameRuntime(null);
        using var cancellation = new CancellationTokenSource();
        view.Dispatch(Entry("typed.run", new { date = "Friday", name = (string?)null }, "batch"), runtime, cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(received[0].Count, Is.EqualTo(2));
            Assert.That(received[0].Day, Is.EqualTo(DayOfWeek.Friday));
            Assert.That(received[0].Name, Is.Null);
            Assert.That(receivedContext!.Runtime, Is.SameAs(runtime));
            Assert.That(receivedContext.ScopeCancellation, Is.EqualTo(cancellation.Token));
            Assert.That(receivedContext.BatchId, Is.EqualTo("batch"));
            Assert.That(receivedContext.Arguments.GetProperty("name").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
        received[0].Count = 9;
        view.Dispatch(Entry("typed.run", new { }), runtime, CancellationToken.None);
        Assert.That(received[1], Is.Not.SameAs(received[0]));
        Assert.That(received[1].Count, Is.EqualTo(2));
        Assert.That(received[1].Day, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(() => view.Dispatch(Entry("typed.run", new { count = 11 }), runtime, CancellationToken.None), Throws.TypeOf<InvalidDataException>());
        Assert.That(received.Count, Is.EqualTo(2), "Invalid arguments never reach the factory.");
    }

    [Test]
    public void TypedFactoryRejectsAnotherDtoContractBeforeExecution()
    {
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("typed", [DefaultPrimitiveEntryBase.ForDto<ObservableFormDto>("typed.run")]));
        registration.RegisterImplementation(new PrimitiveModule([
            PrimitiveImplementation.For<PlainFormDto>("typed.run", (_, _) => throw new AssertionException("Must not execute"))]));
        Assert.That(registration.BuildView, Throws.InvalidOperationException.With.Message.Contains("requires DTO"));
    }

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
    public void SuppliedCatalog_IsUsedExactly_AndCoverageReportsMissingImplementations()
    {
        var catalog = new TargetProfileEntryCatalog([Schema("custom", "custom.first", "custom.missing")]);
        using var registration = new GameRegistration().UseCatalog(catalog);
        Assert.That(registration.BuildCatalog(), Is.SameAs(catalog));
        Assert.That(() => registration.RegisterSchema(Schema("extra", "extra.run")), Throws.InvalidOperationException);
        registration.RegisterInstruction("custom.first", _ => new ImmediatePrimitiveInstance());
        using var view = registration.BuildView();
        Assert.DoesNotThrow(() => view.RequireInstructions(["custom.first"]));
        Assert.That(() => view.RequireInstructions(["custom.missing"]), Throws.InvalidOperationException.With.Message.Contains("custom.missing"));
        Assert.That(view.RequireExecutionCoverage, Throws.InvalidOperationException.With.Message.Contains("custom.missing"));
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
        Assert.That(order, Is.EqualTo(DisposalOrder));
        Assert.DoesNotThrow(view.Dispose);
    }

    private static DefaultEntryModule Schema(string module, params string[] types) =>
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
