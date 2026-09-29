using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class CompositeGameViewTests
{
    private static readonly string[] ExpectedDisposalOrder = ["third", "second", "first"];
    private static readonly string[] ExpectedDisposalErrors = ["third failed", "second failed"];

    [Test]
    public void GameViewExposesOnlySinglePrimitiveDispatchResponsibilities()
    {
        Assert.That(typeof(IGameView).GetInterfaces(), Is.EquivalentTo(new[] { typeof(IDisposable) }));
        Assert.That(typeof(IGameView).GetProperty("ActiveInstances"), Is.Null);
        Assert.That(typeof(IGameView).GetMethod("Advance"), Is.Null);
    }

    [Test]
    public void DispatchNormalizesArgumentsAndBuildsTheCompleteFactoryContext()
    {
        PrimitiveCreateContext? received = null;
        var parameters = new DynamicParameterTable(
        [
            new DynamicParameterDescriptor("visible", typeof(bool), isRequired: true),
            new DynamicParameterDescriptor("opacity", typeof(float), defaultValue: JsonSerializer.SerializeToElement(1f))
        ]);
        var definition = new DefaultPrimitiveEntryBase("layer.show", parameters, context =>
        {
            received = context;
            return new ImmediatePrimitiveInstance(batchId: context.BatchId);
        });
        using var view = new CompositeGameView([new TestModule("layer", [definition])]);
        var runtime = new GameRuntime(null);
        var entry = Entry("layer.show", new { visible = true }, "opening");

        var instance = view.Dispatch(entry, runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance!.IsCompleted, Is.True);
            Assert.That(instance.BatchId, Is.EqualTo("opening"));
            Assert.That(received!.Definition, Is.SameAs(definition));
            Assert.That(received.Entry, Is.Not.SameAs(entry));
            Assert.That(received.Parameters, Is.SameAs(parameters));
            Assert.That(received.Runtime, Is.SameAs(runtime));
            Assert.That(received.Arguments.GetProperty("visible").GetBoolean(), Is.True);
            Assert.That(received.Arguments.GetProperty("opacity").GetSingle(), Is.EqualTo(1f));
            Assert.That(received.BatchId, Is.EqualTo("opening"));
        });
    }

    [Test]
    public void UnknownPrimitiveSafelySkips()
    {
        using var view = new CompositeGameView([]);
        Assert.That(view.Dispatch(Entry("missing.run", new { }), new GameRuntime(null), CancellationToken.None), Is.Null);
    }

    [Test]
    public void ScopeRejectsDuplicateModulesAndPrimitiveNames()
    {
        var first = new TestModule("one", [Definition("custom.run")]);
        var duplicateModule = new TestModule("one", [Definition("custom.other")]);
        var duplicateEntry = new TestModule("two", [Definition("custom.run")]);

        Assert.That(() => new CompositeGameView([first, duplicateModule]), Throws.InvalidOperationException);
        Assert.That(() => new CompositeGameView([first, duplicateEntry]), Throws.InvalidOperationException);
    }

    [Test]
    public void DispatchRejectsAnInstanceWhoseBatchIdDiffersFromTheEntry()
    {
        using var view = new CompositeGameView(
            [new TestModule("test", [new DefaultPrimitiveEntryBase(
                "test.run",
                DynamicParameterTable.Empty,
                _ => new ImmediatePrimitiveInstance(batchId: "wrong"))])]);

        Assert.That(
            () => view.Dispatch(Entry("test.run", new { }, "expected"), new GameRuntime(null), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("BatchId"));
    }

    [Test]
    public void DisposeReleasesModulesInReverseOrderAndAggregatesFailures()
    {
        var disposalOrder = new List<string>();
        var first = new DisposableModule("first", disposalOrder);
        var second = new DisposableModule("second", disposalOrder, new InvalidOperationException("second failed"));
        var third = new DisposableModule("third", disposalOrder, new IOException("third failed"));
        var view = new CompositeGameView([first, second, third]);

        var exception = Assert.Throws<AggregateException>(view.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(disposalOrder, Is.EqualTo(ExpectedDisposalOrder));
            Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(exception.InnerExceptions.Select(error => error.Message),
                Is.EquivalentTo(ExpectedDisposalErrors));
            Assert.That(() => view.Dispatch(Entry("missing.run", new { }), new GameRuntime(null), CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        });

        Assert.DoesNotThrow(view.Dispose);
    }

    [Test]
    public void PrimitiveInstanceDispatchesOnceAndCompletionIsMonotonic()
    {
        var instance = new ControlledInstance(false, false, null);
        var completed = 0;
        instance.Completed += _ => completed++;

        instance.Dispatch();
        instance.Complete();
        instance.Complete();

        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(() => instance.Dispatch(), Throws.InvalidOperationException);
    }

    private static PrimitiveEntry Entry(string type, object arguments, string? batchId = null) =>
        new(type, JsonSerializer.SerializeToElement(arguments), batchId);

    private static PrimitiveEntryBase Definition(string name) => new DefaultPrimitiveEntryBase(
        name,
        DynamicParameterTable.Empty,
        context => new ImmediatePrimitiveInstance(batchId: context.BatchId));

    private sealed class TestModule(string id, IEnumerable<PrimitiveEntryBase> entries) : EntryModuleBase(id, entries);

    private sealed class DisposableModule : EntryModuleBase
    {
        private readonly string _id;
        private readonly ICollection<string> _disposalOrder;
        private readonly Exception? _disposalError;

        public DisposableModule(string id, ICollection<string> disposalOrder, Exception? disposalError = null)
            : base(id, [])
        {
            _id = id;
            _disposalOrder = disposalOrder;
            _disposalError = disposalError;
        }

        public override void Dispose()
        {
            base.Dispose();
            _disposalOrder.Add(_id);
            if (_disposalError is not null) throw _disposalError;
        }
    }

    private sealed class ControlledInstance(bool blocking, bool skippable, string? batchId) : PrimitiveInstance(batchId)
    {
        public override bool IsBlocking { get; } = blocking;
        public override bool IsSkippable { get; } = skippable;
        protected override void OnDispatch() { }
        public void Complete() => TryComplete();
    }
}
