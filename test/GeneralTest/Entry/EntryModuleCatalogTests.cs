using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.View;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Entry;

public class EntryModuleTests
{
    [Test]
    public void ModuleFreezesBothIndependentTables()
    {
        var primitives = new List<PrimitiveEntryBase> { Primitive("custom.first") };
        var composites = new List<CompositeEntryBase> { Composite("authoring.first") };

        var module = new TestEntryModule("authoring", primitives, composites);
        primitives.Add(Primitive("custom.second"));
        composites.Add(Composite("authoring.second"));

        Assert.That(module.PrimitiveEntries.Keys, Is.EqualTo(["custom.first"]));
        Assert.That(module.CompositeEntries.Keys, Is.EqualTo(["authoring.first"]));
        Assert.That(() => ((IDictionary<string, PrimitiveEntryBase>)module.PrimitiveEntries).Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => ((IDictionary<string, CompositeEntryBase>)module.CompositeEntries).Clear(), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void ModuleRejectsDuplicateOrCrossKindDefinitions()
    {
        Assert.That(
            () => new TestEntryModule("test", [Primitive("custom.duplicate"), Primitive("custom.duplicate")]),
            Throws.ArgumentException.With.Message.Contains("more than once"));
        Assert.That(
            () => new TestEntryModule("test", [Primitive("custom.same")], [Composite("custom.same")]),
            Throws.ArgumentException.With.Message.Contains("both primitive and composite"));
    }

    [Test]
    public void DefaultPrimitiveDefinitionCallsItsFactoryForEveryInvocation()
    {
        var created = 0;
        var entry = new DefaultPrimitiveEntryBase(
            "custom.pulse",
            DynamicParameterTable.Empty,
            _ => { created++; return new ImmediatePrimitiveInstance(); });
        var compiled = new PrimitiveEntry(
            "custom.pulse",
            System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            "batch");
        var context = new PrimitiveCreateContext(
            entry,
            compiled,
            new GalNet.Runtime.Runtime.GameRuntime(null),
            CancellationToken.None);

        Assert.That(entry.CreateInstance(context), Is.Not.Null);
        Assert.That(entry.CreateInstance(context), Is.Not.Null);
        Assert.That(created, Is.EqualTo(2));
    }

    [Test]
    public void ProfileMergesModuleTablesAndRejectsCrossModuleTypeCollisions()
    {
        var profile = new TargetProfileEntryCatalog(
        [
            new TestEntryModule("runtime", [Primitive("custom.pulse")]),
            new TestEntryModule("authoring", compositeEntries: [Composite("authoring.expand")])
        ]);

        Assert.That(profile.Definitions.Select(item => item.Type), Is.EquivalentTo(["custom.pulse", "authoring.expand"]));
        Assert.That(
            () => new TargetProfileEntryCatalog(
            [
                new TestEntryModule("one", [Primitive("custom.pulse")]),
                new TestEntryModule("two", [Primitive("custom.pulse")])
            ]),
            Throws.InvalidOperationException.With.Message.Contains("already present"));
    }

    [Test]
    public void RecommendedAnimationModuleContainsPrimitiveAndCompositeTables()
    {
        var modules = BuiltinEntryModules.CreateRecommended();
        var animation = modules.Single(module => module.Id == "animation");

        Assert.That(animation.PrimitiveEntries.Keys, Is.EquivalentTo(["animation.animate", "animation.play", "animation.stop"]));
        Assert.That(animation.CompositeEntries, Is.Not.Empty);
        Assert.That(animation.CompositeEntries.Keys, Does.Contain("transition.crossFade"));
        Assert.That(() => ((IList<IEntryModule>)modules).Clear(), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void MountedViewAndTargetProfileUseTheSamePrimitiveDefinition()
    {
        using var module = new TestEntryModule("runtime", [Primitive("runtime.pulse")], [Composite("authoring.expand")]);
        using var view = new CompositeGameView([module]);
        var profile = new TargetProfileEntryCatalog([module]);

        Assert.That(view.TryGetEntry("runtime.pulse", out var runtimeEntry), Is.True);
        Assert.That(runtimeEntry, Is.SameAs(module.PrimitiveEntries["runtime.pulse"]));
        Assert.That(profile.Get("runtime.pulse").DynamicParameters, Is.SameAs(runtimeEntry!.Parameters));
        Assert.That(profile.Get("authoring.expand").Kind, Is.EqualTo(EntryKind.Composite));
    }

    private static PrimitiveEntryBase Primitive(string type) => new DefaultPrimitiveEntryBase(
        type,
        DynamicParameterTable.Empty,
        static _ => new ImmediatePrimitiveInstance());

    private static CompositeEntryBase Composite(string type) => new DefaultCompositeEntryBase(
        type,
        DynamicParameterTable.Empty,
        () => new TestCompositeEntry(type));

    private sealed class TestCompositeEntry(string type) : CompositeEntry
    {
        public override string Type => type;
        public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) => [];
    }

    private sealed class TestEntryModule(
        string id,
        IEnumerable<PrimitiveEntryBase>? primitiveEntries = null,
        IEnumerable<CompositeEntryBase>? compositeEntries = null)
        : EntryModuleBase(id, primitiveEntries, compositeEntries);
}
