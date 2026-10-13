using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Entry;

public class EntryModuleTests
{
    private static readonly int[] InvalidSkipPoints = [4, 2];

    [Test]
    public void BuiltinContractsValidateCustomImplementationsBeforeFactoryInvocation()
    {
        var called = false;
        using var view = new GameRegistration().AddBuiltinSchemas()
            .RegisterInstruction("video.play", context =>
            {
                called = true;
                return new ImmediatePrimitiveInstance(() => { }, batchId: context.BatchId);
            }).BuildView();
        Assert.Throws<InvalidDataException>(() => view.Dispatch(new PrimitiveEntry("video.play",
            JsonSerializer.SerializeToElement(new { asset = "clip", skipPoints = InvalidSkipPoints })),
            new GameRuntime(null), CancellationToken.None));
        Assert.That(called, Is.False);
    }

    [Test]
    public void AnimationContractRejectsUnknownEmbeddedEvents()
    {
        var schema = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()).Get("animation.play");
        Assert.Throws<InvalidDataException>(() => PrimitiveArgumentHelper.Normalize(schema.DynamicParameters!,
            JsonSerializer.SerializeToElement(new
            {
                plan = new { durationFrames = 1, events = new[] { new { frame = 0, type = "custom.unsupported", parameters = new { } } } }
            })));
    }
    [Test]
    public void SchemaOnlyContainsAllBuiltinContractsAndNoRuntimeFactories()
    {
        using var registration = new GameRegistration().AddBuiltinSchemas();
        var catalog = registration.BuildCatalog();
        Assert.That(catalog.TryGet("audio.play", out _), Is.True);
        Assert.That(catalog.TryGet("video.play", out _), Is.True);
        Assert.That(catalog.Get("transition.crossFade").Kind, Is.EqualTo(EntryKind.Composite));
        Assert.That(typeof(PrimitiveEntryBase).GetMethod("CreateInstance"), Is.Null);
        Assert.That(typeof(IEntryModule).GetInterfaces(), Does.Not.Contain(typeof(IDisposable)));
        using var view = registration.BuildView();
        Assert.That(view.CanExecute("variable.set"), Is.False);
    }

    [Test]
    public void PartialBuiltinImplementationDoesNotRegisterOtherDomains()
    {
        using var registration = new GameRegistration().AddBuiltinSchemas().AddBuiltinVariables();
        using var view = registration.BuildView();
        var runtime = new GameRuntime(null);
        view.Dispatch(new PrimitiveEntry("variable.set", JsonSerializer.SerializeToElement(new { target = "score", expression = "3" })),
            runtime, CancellationToken.None);
        Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(3));
        Assert.That(view.CanExecute("audio.play"), Is.False);
    }

    [Test]
    public void CustomImplementationCanUseBuiltinSchemaWithoutBuiltinExecution()
    {
        var calls = 0;
        using var registration = new GameRegistration().AddBuiltinSchemas()
            .RegisterInstruction("audio.play", context => new ImmediatePrimitiveInstance(() => calls++, batchId: context.BatchId));
        using var view = registration.BuildView();
        view.Dispatch(new PrimitiveEntry("audio.play", JsonSerializer.SerializeToElement(new { asset = "clip" })), new GameRuntime(null), CancellationToken.None);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(view.CanExecute("variable.set"), Is.False);
    }

    [Test]
    public void MainBuiltinRegistrationAndSeparateRegistrationExposeTheSameInstructions()
    {
        using var main = new GameRegistration().AddBuiltins().BuildView();
        using var separateRegistration = new GameRegistration().AddBuiltinSchemas();
        using var separate = separateRegistration.AddBuiltinImplementations(separateRegistration.BuildCatalog()).BuildView();
        Assert.That(main.Primitives.Select(entry => entry.Type), Is.EquivalentTo(separate.Primitives.Select(entry => entry.Type)));
        Assert.That(main.Primitives.All(entry => main.CanExecute(entry.Type)), Is.True);
    }

    [Test]
    public void SelectedBuiltinExecution_DoesNotExtendPartialContract_AndStillSharesState()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateVariablesModule()]);
        using var registration = new GameRegistration().UseCatalog(catalog).AddBuiltinImplementations(catalog);
        using var view = registration.BuildView();
        Assert.DoesNotThrow(view.RequireExecutionCoverage);
        Assert.That(view.Primitives.Select(definition => definition.Type), Is.EquivalentTo(catalog.Definitions.Select(definition => definition.Type)));
        Assert.That(view.CanExecute("audio.play"), Is.False);
        var runtime = new GameRuntime(null);
        view.Dispatch(new PrimitiveEntry("variable.set", JsonSerializer.SerializeToElement(new { target = "score", expression = "3" })), runtime, CancellationToken.None);
        view.Dispatch(new PrimitiveEntry("variable.set", JsonSerializer.SerializeToElement(new { target = "score", expression = "[score] + 2" })), runtime, CancellationToken.None);
        Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(5));
    }

    [Test]
    public void SchemaModuleFreezesItsTablesAndRejectsDuplicateFullIds()
    {
        var entries = new List<PrimitiveEntryBase> { new DefaultPrimitiveEntryBase("custom.first", DynamicParameterTable.Empty) };
        var module = new DefaultEntryModule("custom", entries);
        entries.Clear();
        Assert.That(module.PrimitiveEntries, Has.Count.EqualTo(1));
        Assert.That(() => ((IDictionary<string, PrimitiveEntryBase>)module.PrimitiveEntries).Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => new TargetProfileEntryCatalog([module, module]), Throws.InvalidOperationException);
    }
}
