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

    [TestCase(false)]
    [TestCase(true)]
    public void RegistryCannotBeChangedAfterCompositionEvenWhenCompositionFails(bool fail)
    {
        PrimitiveRegistry? captured = null;
        using var registration = new GameRegistration().RegisterSchema(Schema("test", "test.run", "test.late"))
            .RegisterImplementation(new PrimitiveModule(registry =>
            {
                captured = registry;
                registry.Register("test.run", _ => new ImmediatePrimitiveInstance());
                if (fail) registry.Register("test.run", _ => new ImmediatePrimitiveInstance());
            }));
        if (fail) Assert.That(registration.BuildView, Throws.InvalidOperationException.With.Message.Contains("already registered"));
        else registration.BuildView().Dispose();
        Assert.That(() => captured!.Register("test.late", _ => new ImmediatePrimitiveInstance()), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void SelectedModuleFiltersByItsCatalogAndRestoresStrictRegistration()
    {
        var selection = new TargetProfileEntryCatalog([Schema("selected", "test.run")]);
        using var inner = new PrimitiveModule(registry =>
        {
            registry.Immediate("test.run", _ => { });
            registry.Immediate("test.other", _ => throw new AssertionException("Not selected."));
        });
        var widerCatalog = new TargetProfileEntryCatalog([Schema("all", "test.run", "test.other")]);
        using var selected = new PrimitiveModule(registry => registry.RegisterDeclared(inner, widerCatalog));
        using var registration = new GameRegistration().RegisterSchema(Schema("all", "test.run", "test.other"))
            .RegisterImplementation(new PrimitiveModule(registry =>
            {
                registry.RegisterDeclared(selected, selection);
                Assert.That(() => registry.Immediate("test.unknown", _ => { }), Throws.InvalidOperationException.With.Message.Contains("no primitive schema"));
            }));
        using var view = registration.BuildView();
        Assert.That(view.CanExecute("test.run"), Is.True);
        Assert.That(view.CanExecute("test.other"), Is.False);
    }

    [Test]
    public void SelectedModuleStillRejectsWrongDtoAndFactoriesCannotReturnNull()
    {
        var catalog = new TargetProfileEntryCatalog([Schema("test", "test.run")]);
        using var selected = new PrimitiveModule(registry => registry.Immediate<PlainFormDto>("test.run", (_, _) => { }));
        using var wrong = new GameRegistration().UseCatalog(catalog)
            .RegisterImplementation(new PrimitiveModule(registry => registry.RegisterDeclared(selected, catalog)));
        Assert.That(wrong.BuildView, Throws.InvalidOperationException.With.Message.Contains("requires DTO"));
        using var registration = new GameRegistration().UseCatalog(catalog).RegisterInstruction("test.run", _ => null!);
        using var view = registration.BuildView();
        Assert.That(() => view.Dispatch(Entry("test.run", new { }), new GameRuntime(null), default),
            Throws.InvalidOperationException.With.Message.Contains("returned no instance"));
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task TypedAsyncFactoryPropagatesDefaultsBatchScopeAndMode(PrimitiveInvocationMode mode)
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("typed",
            [DefaultPrimitiveEntryBase.ForDto<PlainFormDto>("typed.async")]));
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Async<PlainFormDto>("typed.async", (_, options, token) =>
            {
                Assert.That(options.Count, Is.EqualTo(2));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                calls.Add("normal");
                return pending.Task;
            }, blocking: true, catchUp: (_, _, _) => { calls.Add("catchup"); return Task.CompletedTask; })));
        using var view = registration.BuildView();
        var instance = view.Dispatch(Entry("typed.async", new { }, "batch"), new GameRuntime(null), cancellation.Token, mode)!;
        Assert.That(instance.BatchId, Is.EqualTo("batch"));
        Assert.That(instance.IsBlocking, Is.True);
        Assert.That(instance.IsSkippable, Is.False);
        Assert.That(instance.IsCompleted, Is.EqualTo(mode == PrimitiveInvocationMode.CatchUp));
        pending.SetResult();
        await instance.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calls, Is.EqualTo(new[] { mode == PrimitiveInvocationMode.CatchUp ? "catchup" : "normal" }));
    }

    [Test]
    public void GeneratedJsonDefaultsChoicesAndStringEncodingsReachTypedExecution()
    {
        var received = new List<ExtendedFieldOptions>();
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("extended",
            [DefaultPrimitiveEntryBase.ForDto<ExtendedFieldOptions>("extended.run")]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate<ExtendedFieldOptions>("extended.run", (_, options) => received.Add(options))));
        using var view = registration.BuildView();
        view.Dispatch(Entry("extended.run", new { }), new GameRuntime(null), default);
        view.Dispatch(Entry("extended.run", new { enabled = "false", day = "Friday", side = "Right" }), new GameRuntime(null), default);
        Assert.That(received[0].Numbers, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(received[0].Enabled, Is.True);
        Assert.That(received[0].Day, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(received[1].Enabled, Is.False);
        Assert.That(received[1].Day, Is.EqualTo(DayOfWeek.Friday));
        Assert.That(received[1].Side, Is.EqualTo("Right"));
        received[0].Numbers!.Add(3);
        Assert.That(received[1].Numbers, Has.Count.EqualTo(2));
        Assert.That(() => view.Dispatch(Entry("extended.run", new { side = "Up" }), new GameRuntime(null), default), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => view.Dispatch(Entry("extended.run", new { enabled = true }), new GameRuntime(null), default), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void DocumentedNoParameterExampleCompilesAndAppliesToRuntime()
    {
        const string type = "game.mark";
        using var registration = new GameRegistration()
            .RegisterSchema(new DefaultEntryModule("game", [new DefaultPrimitiveEntryBase(type, DynamicParameterTable.Empty)]))
            .RegisterImplementation(new PrimitiveModule(registry => registry.Immediate(type, context => context.Runtime.SetVariable("seen", true))));
        using var view = registration.BuildView();
        var compiled = GalNet.Core.Compilation.GalgroupCompiler.Compile(new GalNet.Core.Serialization.GroupDocument
        {
            Entries = [new() { Id = "mark", Type = type }]
        }, registration.BuildCatalog()).Document.Entries.Single();
        var runtime = new GameRuntime(null);
        view.Dispatch(new PrimitiveEntry(compiled.TypeId, compiled.Arguments, compiled.BatchId), runtime, default);
        Assert.That(runtime.GetVariable("seen")!.AsBool(), Is.True);
    }

    [Test]
    public void TypedImmediateCatchUpOverrideReceivesDecodedOptionsAndSuppressesNormalSideEffect()
    {
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("typed",
            [DefaultPrimitiveEntryBase.ForDto<PlainFormDto>("typed.run")]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate<PlainFormDto>("typed.run", (_, _) => throw new AssertionException("Normal side effect."),
                (context, options) => context.Runtime.SetVariable("count", options.Count))));
        using var view = registration.BuildView();
        var runtime = new GameRuntime(null);
        view.Dispatch(Entry("typed.run", new { }), runtime, default, PrimitiveInvocationMode.CatchUp);
        Assert.That(runtime.GetVariable("count")!.AsInt(), Is.EqualTo(2));
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void ImmediateFactoryPropagatesBatchAndUsesExplicitCatchUpPolicy(PrimitiveInvocationMode mode)
    {
        var calls = new List<string>();
        using var registration = new GameRegistration().RegisterSchema(Schema("sync", "sync.run"));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate("sync.run", context => calls.Add("normal:" + context.BatchId),
                context => calls.Add("catchUp:" + context.BatchId))));
        using var view = registration.BuildView();
        var instance = view.Dispatch(Entry("sync.run", new { }, "batch"), new GameRuntime(null), default, mode);
        Assert.That(calls, Is.EqualTo(new[] { (mode == PrimitiveInvocationMode.CatchUp ? "catchUp:" : "normal:") + "batch" }));
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(instance.IsBlocking || instance.IsSkippable, Is.False);
        Assert.That(instance.BatchId, Is.EqualTo("batch"));
        Assert.That(() => instance.Dispatch(), Throws.InvalidOperationException);
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void TypedImmediateFactoryUsesCompilerDefaultsConverterAndCustomValidation(PrimitiveInvocationMode mode)
    {
        var received = new List<PlainFormDto>();
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("typed", [DefaultPrimitiveEntryBase.ForDto<PlainFormDto>("typed.run",
            arguments => { if (arguments.TryGetProperty("count", out var count) && count.GetInt32() == 4) throw new ArgumentException("Reserved count."); })]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate<PlainFormDto>("typed.run", (_, options) => received.Add(options))));
        var catalog = registration.BuildCatalog();
        var compiled = GalNet.Core.Compilation.GalgroupCompiler.Compile(new GalNet.Core.Serialization.GroupDocument
        {
            Entries = [new() { Id = "one", Type = "typed.run", Parameters = new() { ["date"] = JsonSerializer.SerializeToElement("Friday") } }]
        }, catalog).Document.Entries.Single();
        using var view = registration.BuildView();
        var instance = view.Dispatch(new PrimitiveEntry(compiled.TypeId, compiled.Arguments, "batch"), new GameRuntime(null), default, mode);
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(received[0].Count, Is.EqualTo(2));
        Assert.That(received[0].Day, Is.EqualTo(DayOfWeek.Friday));
        received[0].Count = 10;
        view.Dispatch(Entry("typed.run", new { }), new GameRuntime(null), default, mode);
        Assert.That(received[1].Count, Is.EqualTo(2));
        Assert.That(received[1], Is.Not.SameAs(received[0]));
        Assert.That(() => view.Dispatch(Entry("typed.run", new { count = 4 }), new GameRuntime(null), default, mode), Throws.ArgumentException);
        Assert.That(() => view.Dispatch(Entry("typed.run", new { extra = 1 }), new GameRuntime(null), default, mode), Throws.TypeOf<InvalidDataException>());
        Assert.That(received, Has.Count.EqualTo(2));
    }

    [Test]
    public void TypedImmediateFactoryRejectsWrongContractAndDoesNotSwallowDispatchFailure()
    {
        using var wrong = new GameRegistration().RegisterSchema(Schema("typed", "typed.run"));
        wrong.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate<PlainFormDto>("typed.run", (_, _) => { })));
        Assert.That(wrong.BuildView, Throws.InvalidOperationException.With.Message.Contains("requires DTO"));
        using var registration = new GameRegistration().RegisterSchema(Schema("sync", "sync.fail"))
            .RegisterImplementation(new PrimitiveModule(registry => registry.Immediate("sync.fail", _ => throw new IOException("failed"))));
        using var view = registration.BuildView();
        Assert.That(() => view.Dispatch(Entry("sync.fail", new { }), new GameRuntime(null), default),
            Throws.TypeOf<IOException>().With.Message.EqualTo("failed"));
    }

    [Test]
    public void TypedFactoryUsesNormalizedDefaultsConvertersAndIndependentDtoInstances()
    {
        var received = new List<PlainFormDto>();
        PrimitiveCreateContext? receivedContext = null;
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("typed", [DefaultPrimitiveEntryBase.ForDto<PlainFormDto>("typed.run")]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Register<PlainFormDto>("typed.run", (context, options) =>
            {
                receivedContext = context;
                received.Add(options);
                return new ImmediatePrimitiveInstance(batchId: context.BatchId);
            })));
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
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Register<PlainFormDto>("typed.run", (_, _) => throw new AssertionException("Must not execute"))));
        Assert.That(registration.BuildView, Throws.InvalidOperationException.With.Message.Contains("requires DTO"));
    }

    [Test]
    public async Task AsyncDisposalThroughGameViewInterfaceUsesModuleAsyncCleanup()
    {
        var module = new AsyncDisposableModule();
        await using (IPrimitiveDispatcher view = new GameRegistration().RegisterImplementation(module).BuildView())
            Assert.That(module.CleanedUp, Is.False);
        Assert.That(module.CleanedUp, Is.True);
    }

    private sealed class AsyncDisposableModule() : PrimitiveModule(_ => { })
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

    private sealed class DisposableModule(int id, List<int> order, bool fail = false) : PrimitiveModule(_ => { })
    {
        public override void Dispose()
        {
            order.Add(id);
            if (fail) throw new IOException("expected");
            base.Dispose();
        }
    }
}

[GalNet.Core.Parameters.GenerateFieldSchema]
public partial class ExtendedFieldOptions
{
    [GalNet.Core.Parameters.Field(0, DefaultJson = "[1,2]", EditorId = "json")]
    public List<int>? Numbers { get; set; }
    [GalNet.Core.Parameters.Field(1, AllowNull = true, EditorId = "choice"), System.Text.Json.Serialization.JsonConverter(typeof(GalNet.Core.Parameters.StringBooleanValueConverter))]
    public bool? Enabled { get; set; } = true;
    [GalNet.Core.Parameters.Field(2, AllowNull = true, EditorId = "choice"), System.Text.Json.Serialization.JsonConverter(typeof(GalNet.Core.Parameters.StringEnumValueConverter<DayOfWeek>))]
    public DayOfWeek? Day { get; set; } = DayOfWeek.Monday;
    [GalNet.Core.Parameters.Field(3, Choices = new[] { "Left", "Right" })]
    public string? Side { get; set; } = "Left";
}
